# إضافة الخوارزميات 6 — Transactional Outbox + إرسال موثوق + Retry

الأساس: `98d5ff3` (المراحل 1–5 مدمجة). لا يوجد ABP Outbox/Distributed Event Bus في المشروع؛ الموجود هو Outbox المرحلة 8 وقد طُوّر، لا مسار ثانٍ.

## ما كان موجودًا وما تغيّر
| الجزء | قبل | بعد |
|---|---|---|
| تسجيل الحدث `OutboxManager.EnqueueAsync` | EF Insert في UoW الطلب؛ تكرار المفتاح يُفشل العملية حتى لو المحتوى نفسه، ولا فهرس فريد | INSERT … ON CONFLICT بنفس اتصال ومعاملة EF الحاليين؛ نفس المفتاح والمحتوى (مساواة jsonb) = الصف نفسه، محتوى مختلف = `409 Saham:OutboxEventKeyConflict` ويفشل التغيير كله. فهرس فريد `UX_AppOutboxEvents_EventKey`. بلا معاملة = استثناء |
| الـrelay (حدث ← إشعارات) | قراءة ids ثم معالجة؛ نسختان من الخادم قد تعالجان الحدث نفسه | `FOR UPDATE SKIP LOCKED` لكل حدث داخل معاملة قصيرة (تخطيط + PublishedAt) بلا اتصال خارجي |
| الإرسال (`AppNotifications`، صف = قناة + مستلم) | لا حجز؛ نسختان قد ترسلان SMS نفسه؛ الاتصال بالمزوّد داخل معاملة مفتوحة؛ backoff بلا jitter؛ نص الخطأ يُخزّن ويُسجّل | محرك الحزمة `OutboxProcessor` + `NotificationOutboxStore`: حجز صف واحد بـ`SKIP LOCKED` و`LeaseToken`/`LeaseUntil` بوقت قاعدة البيانات؛ الإرسال خارج أي معاملة بمهلة `SendTimeout`؛ التثبيت مشروط بملكية lease غير منتهية؛ Equal Jitter بسقف؛ `Retry-After` حد أدنى؛ رموز أخطاء آمنة فقط |
| المزوّد | — | `NotificationSender` (`IOutboxSender`) يستدعي `ISmsSender`/`IPushSender` الحقيقيين (FCM عند تهيئته)؛ معرّف الإشعار = مفتاح idempotency (خاصية SMS، collapse key/tag في FCM/APNs) |

مسار الحدث: تغيير الطلب ← `OrderChangedEto` (نفس UoW) ← `OutboxEventHandlers` ← `OutboxManager.EnqueueAsync` (نفس المعاملة) ← COMMIT ← `NotificationDispatchWorker` (relay بـSKIP LOCKED ← `NotificationPlanner` ← صفوف `AppNotifications` فريدة لكل event/channel/user مع NULLS NOT DISTINCT) ← `NotificationQueue.RunOnceAsync` ← `OutboxProcessor` ← `NotificationSender` ← المزوّد.

العامل: `NotificationDispatchWorker` (ABP periodic، كل 5 ث، دفعة 50) هو المرسل الوحيد؛ لم يُسجّل `OutboxBackgroundService` من الحزمة حتى لا يوجد عاملان. خطأ DB ينهي الدورة ويُسجّل نوعه فقط، والدورة التالية تستأنف. الإيقاف أثناء الإرسال لا يسجل نجاحًا؛ lease ينتهي ويُستعاد.

## الإعدادات (افتراضات تشغيلية قابلة للتغيير)
`Saham.Notifications.MaxAttempts`=3، `RetryBaseSeconds`=30 (أول إعادة 15–30 ث)، `MaxRetryDelaySeconds`=900، `LeaseSeconds`=90، `SendTimeoutSeconds`=20. تُتحقق عند كل دورة (lease > timeout + 10 ث).

## المراقبة وإعادة DEAD
- `GET /api/app/notification/health` (صلاحية `Notifications.View`، بلا payload/هواتف/رموز): حسب الحالة، DEAD، المستحقة وعمر أقدمها، leases منتهية، أحداث غير منشورة وعمر أقدمها، أحداث فشل تخطيطها، وحالة عامل هذه النسخة (آخر تشغيل/خطأ، الإخفاقات المتتالية، leases المفقودة).
- `POST /api/app/notification/{id}/retry` (صلاحية `Notifications.Retry`، يُسجّل في Audit Log): DEAD فقط، بنفس الصف وEventKey والمعرّف؛ SENT مرفوض.
- SMS رمز التسليم إن أصبح DEAD يفتح حادثة `DeliveryCodeSmsFailed` كما سابقًا.

## الـmigration
`Phase11_OutboxReliability` (تراكمية): `LeaseToken`/`LeaseUntil` في `AppNotifications` + فهرس leases + قيد اتساق الحالة؛ فهرس فريد لـ`EventKey` في `AppOutboxEvents` بعد إعادة تسمية أي مفاتيح مكررة قديمة إلى `…:dup:{Id}` (لا حذف، والحالة المنشورة تبقى، فلا يُعاد الإرسال)؛ `IdempotencyKey` في `AppSmsLogs` + فهرس فريد جزئي. طُبّقت على `saham_test` فقط.

## حدود الضمان
- **At-least-once**: قبول المزوّد ثم سقوط الخادم قبل التثبيت يعيد الإرسال بالمفتاح نفسه. محوّل SMS الحالي (`LoggingSmsSender`) يُسقط المكرر بالمفتاح؛ مزوّد SMS حقيقي (EXT-02 لم يُختر) يجب أن يدعم مفتاح idempotency أو يبقى التكرار ممكنًا. FCM لا يدعم مفتاح idempotency: collapse key/tag يمنع تكرار العرض على الجهاز فقط (best effort). لا ادعاء exactly-once.
- `SendTimeout` يقطع الانتظار (`WaitAsync`) لكن `ISmsSender` في ABP لا يقبل CancellationToken؛ timeout = نتيجة مجهولة تُعاد بنفس المفتاح.
- SENT = قبول جهة الإرسال، لا وصول الرسالة. لا ترتيب صارم بين رسائل الحدث الواحد؛ العميل يعيد قراءة الحالة من الخادم (DER-NTF-02).
- رمز التسليم لا يُخزّن في الإشعار ولا في payload؛ يُقرأ مشفرًا من الطلب عند الإرسال. `LoggingSmsSender` (بيئة التطوير) يحفظ النص في `SmsLogs` كما في التصميم السابق.
- لا Multi-tenancy في المشروع.
- أحداث فشل تخطيطها 5 مرات تُعلَّم منشورة مع الخطأ (سلوك المرحلة 8) وتظهر في المراقبة؛ لا يوجد بعد إجراء إعادة لها.
- لا يُستعمل العامل لأي خصم/استرداد مالي؛ المال يبقى في state machine والمصالحة الحالية.

## التحقق (فعلي)
- `dotnet build Saham.sln -c Release`: نجح، 19 تحذيرًا قديمًا.
- اختبارات الحزمة (`test/Saham.Outbox.Tests`): 31/31 — 24 منقولة كما هي + 7 للتعديلات. أُصلح خطأ في `FakeStore` بالحزمة (`LastClaim` كان يُستبدل بـnull فيرمي NullReferenceException). لم يُنقل اختبارا `OutboxBackgroundService` لأن العامل المستخدم ABP worker.
- `test/Saham.Outbox.PostgresChecks` على `saham_test` (اتصالات ومعاملات مستقلة): 28/28 — بنود INTEGRATION_CHECKLIST 1–18 + ثلاث نسخ معالجة متزامنة + DEAD callback. البند 19: لا ABP Outbox (لا ازدواجية).
- E2E عبر الخادم: 11/11 — رحلة طلب حتى إرسال الإشعار، rollback الطلب مع الحدث، الاستئناف بعد فشل DB، المراقبة، إعادة DEAD بنفس المفتاح، رفض إعادة SENT، سجل التدقيق.
- اختبارات المراحل 1–5: الوحدوية كلها ناجحة؛ نتائج تكامل الإعادة في تقرير التسليم.
