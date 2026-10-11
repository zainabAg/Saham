# تسليم الجلسة — مشروع سهم (Saham)

> آخر تحديث: 2026-10-11 (بعد إنجاز المرحلة 8). اقرأ هذا الملف + `NEXT-STEPS.md` + `docs/backend-plan.md` قبل أي شغل بجلسة جديدة.

---

## 1. ملخص المشروع

**سهم** منصة توصيل طلبات مطاعم في كربلاء (زبون، مطعم/تابلت، سائق، عمليات، محاسب، مالك).
هذا المستودع = الـ **Backend** فقط.

| البند | القيمة |
|---|---|
| المرجع | `C:\Users\BEST LAPTOP\Desktop\شركة السهم\Sahm-SRS-v1.3.pdf` (265 صفحة، عربي) — الأقسام 5، 10، 12، 13، 14، 15، والملحق C (نطاق الـ MVP) |
| الخطة | `docs/backend-plan.md` (المراحل 0–10 بـ checkboxes) |
| الإطار | **ABP 9.2** — مشروع **Single-Layer** في `Saham/` (.NET) |
| ORM | EF Core + Npgsql + **NetTopologySuite** |
| قاعدة البيانات | **Neon PostgreSQL + PostGIS** — الاتصال في `Saham/appsettings.secrets.json` (غير متتبع في git) |
| الهوية | OpenIddict + extension grant `phone_otp` |
| الفوري | `Volo.Abp.AspNetCore.SignalR` (`/signalr-hubs/orders`) |
| الرسائل | `LoggingSmsSender` يكتب في جدول `SmsLog` (لا مزود حقيقي بعد — EXT-02) |
| المهام الخلفية | `AsyncPeriodicBackgroundWorkerBase` في `Saham/BackgroundWorkers/` |
| العنوان المحلي | `https://localhost:44343` (Swagger على `/swagger`) |

### هيكل الكود (نمط ثابت لكل وحدة)
- `Entities/<Module>/` — الكيانات + Enums + `*Manager` (Domain Services) + Local Event handlers.
- `Data/Configurations/<Module>Configuration.cs` — Fluent API لكل وحدة (extension على `ModelBuilder`).
- `Services/<Actor>/` — AppService لكل **فاعل** (Customers, Restaurants, Drivers, Ops, Finance, Admin, Account).
- `Services/Dtos/<Module>/` — DTOs. `ObjectMapping/SahamAutoMapperProfile.cs`.
- `Permissions/`, `Settings/`, `SahamErrorCodes.cs`, `Localization/Saham/{ar,en}.json`.
- `Hubs/` — `OrdersHub` + Notifiers (ترسل بعد الـ commit فقط عبر `uow.OnCompleted`).
- `Identity/CurrentScope.cs` — نطاق المستخدم (مطعمه، سائقه، زبونه).
- `Infrastructure/Idempotency/` — `[Idempotent]` + `IdempotencyRecord`.

---

## 2. المراحل المنجزة

| المرحلة | المحتوى | الكومت |
|---|---|---|
| 0 + 1 | الأساس، الهوية، الأدوار الثمانية، OTP، SmsLog، Idempotency، HousekeepingWorker | `a7f94b2` (و`0bf48d9` لدعم `appsettings.secrets.json`) |
| 2 | الكتالوج والجغرافيا (مطاعم، منيو، مدن، مناطق، عناوين) + حذف مثال Book | `a2d22b3` |
| 3 | التسعير وعرض السعر (`PricingPlan`, `Quote`, `QuoteSnapshot`) | `ae34437` |
| 4 | الطلب ودورة حياته عند المطعم (`Order`, `OrderStateMachine`, `OrdersHub`, `OrderTimeoutsWorker`) | `ad3cfba` |
| 5 | السائق والتوزيع (`Driver`, `DispatchManager`, `DispatchWorker`, `Incident` NO_DRIVER) | `309a3e9` |
| 6 | الاستلام والتسليم (`DeliveryManager`, رمز التسليم، `DriverStaleWorker`، التتبع) | `76b177b` |
| 7 | المال (`LedgerManager`, القيود، Triggers، `CashManager`, الأرصدة، «لم يُستلم») | `baf5122` ← **آخر كومت** |
| 8 | لوحة العمليات، الحوادث، سجل التدقيق، الـ Outbox والإشعارات | **منجزة ومختبرة — بانتظار كومت المستخدم** |

الفرع الحالي: `Develop` (الفرع الرئيسي `main`). Migrations كلها مطبقة على Neon (آخرها `Phase8_Ops_Audit_Outbox`).

البنود المؤجلة صراحةً من المراحل السابقة (موجودة كـ `[ ]` في الخطة):
- الكوبونات ← المرحلة 9.
- الدفع بالمحفظة `J-WALLET-*` والاسترداد، الكشوف الرسمية ودفع الخميس ← المرحلة 9.

---

## 3. المرحلة الثامنة — منجزة (بانتظار الكومت)

### القرارات اللي أخذها المستخدم بهاي المرحلة (نهائية)
- **سجل التدقيق = (أ)**: عرض فوق جداول ABP (`AbpAuditLogs`, `AbpEntityChanges`) + `Reason` و`ActorRoles` بالـ `ExtraProperties`. **ماكو** جدول خاص ولا سلسلة بصمات `prev_hash/hash` ولا Trigger.
- **Push**: `IPushSender` + `LoggingPushSender` (يكتب بسجل التطبيق) لحين مفاتيح Firebase.
- **Outbox**: جدولين `AppOutboxEvents` (bigserial) + `AppNotifications` مع `UK(EventKey, Channel, UserId) NULLS NOT DISTINCT`.
- **لغة الإشعار**: القالب يُترجم وقت الإرسال بـ `ar` (ماكو حقل لغة مفضلة بعد).

### شنو انبنى
| الجزء | الملفات |
|---|---|
| الحوادث | `Entities/Support/Incident.cs` (أنواع جديدة: `DeliveryCodeSmsFailed`, `ScheduledRestaurantClosed`, `Other`؛ `AssignTo`, `UpdateNote`, `ResolvedBy`, حدث `IncidentChangedEto`)، `IncidentManager` (`OpenOnceAsync`, `OpenManualAsync`, `AutoResolveForOrderAsync`)، `IncidentOrderEventHandler` (إغلاق آلي + حوادث الإلغاء)، `Hubs/IncidentRealtimeNotifier.cs` |
| الـ Workers | `OrderTimeoutsWorker`: تنبيه عدم الرد بعد 2 د، `OrderLate` + إشعار الزبون، `ItemDecisionTimeout`. `NotificationDispatchWorker` (كل 5 ث): ينشر الـ outbox ويرسل المستحق |
| لوحة العمليات | `OpsOrderAppService.GetLiveAsync`، `OpsDriverAppService`، `OpsRestaurantAppService`، `IncidentAppService` (فتح/إقرار/إسناد/ملاحظة/حل، المحاسب = المالية فقط)، `RestaurantProfileAppService.HeartbeatAsync` + `TabletPresenceTracker` (`ExecuteUpdate` بدون لمس ConcurrencyStamp)، `OrdersHub` (مجموعة `finance` + تسجيل التابلت عند الاتصال) |
| التدقيق | `Infrastructure/Auditing/SahamAuditLogContributor.cs` (يستخرج `reason/note/resolution` من بارامترات الطلب)، `Services/Admin/AuditLogAppService.cs` (`GetList` بفلتر `operation` + `GetEntityHistory`، الـ enums بأسمائها)، Entity History صار يشمل `IdentityUser/Role`, `PermissionGrant`, `Setting` |
| الإشعارات | `Entities/Notifications/`: `OutboxEvent`, `Notification`, `OutboxManager`, `OutboxEventHandlers` (طلب + سائق)، `NotificationPlanner` (مصفوفة I.2-6)، `NotificationSender` (رندر + إرسال + إعادة + حادثة رمز التسليم)، `NotificationOptions`؛ `Integrations/Push/`؛ `Services/Admin/NotificationAppService.cs` |
| أحداث صريحة للـ outbox | `DeliveryManager` (`CUSTOMER_CASH_BANNED`؛ و**SMS الرمز انشال** منه وصار عبر `ORDER_PICKED_UP`)، `CashManager` (`DRIVER_CASH_OVERDUE`)، `DriverReviewAppService` (`DRIVER_REVIEWED`) |
| صلاحيات/إعدادات | `Saham.OpsBoard.View`، `Saham.Notifications.View/Retry` (عمليات + مالك)، `Incidents.Handle` للمحاسب؛ `Saham.Restaurants.TabletOfflineSeconds`=120، `Saham.Notifications.MaxAttempts`=3، `RetryBaseSeconds`=30 |

### ملاحظات تصميم مهمة
- الـ **Realtime** (SignalR) بقى مباشر بعد الـ commit مثل قبل؛ الـ Outbox يخص **PUSH و SMS** بس (الدائمة).
- نص الإشعار ما يُخزن: `Template` + `Args`؛ رمز التسليم يُقرأ من الطلب (`DeliveryOtpProtected`) وقت الإرسال، فما ينحفظ بجدول الإشعارات.
- `Notifications:SimulateFailure` (إعداد تطبيق، مو Setting) — للاختبار فقط: `Notifications__SimulateFailure__0=Sms` يخلي كل SMS يفشل.
- فلتر التدقيق اسمه `operation` مو `action` — `action` اسم محجوز بـ MVC route values (كان يرجع صفر نتائج).

### نتيجة التحقق على Neon
- `phase8_tests.py` (تشغيل A: `RestaurantAlertMinutes=1`, `LateThresholdMinutes=-120`, `RetryBaseSeconds=3`): **54/55**، والواحد الفاشل (الـ enum يظهر رقم بالتدقيق) انصلح وتأكدنا منه يدوياً (`Placed → Cancelled`).
- `phase8_failure.py` (تشغيل B: `Notifications__SimulateFailure__0=Sms`): **11/11** — 3 محاولات بفواصل متزايدة ← `Dead` ← حادثة `DeliveryCodeSmsFailed` ← علم `NOTIFICATION_FAILED` باللوحة ← إعادة إرسال يدوية.
- السجلات نظيفة، وما بقى `Saham.exe` يتيم.
- السكربتات بـ: `C:\Users\BESTLA~1\AppData\Local\Temp\claude\E--abp-projects-Saham\ff7f1f2e-ea8f-4495-acf3-4807969264ac\scratchpad\` (مجلد مؤقت).

---

## 4. حالة git
- كل شغل المرحلة 8 **غير متكومت** (المستخدم يسوي الكومت). يشمل Migration `20261011111539_Phase8_Ops_Audit_Outbox` (مطبقة على Neon).
- `M .gitignore` (سطر `appsettings.secrets.json`) و`docs/handoff/` — جاهزة للكومت ويا المرحلة.

---

## 5. القرارات النهائية (لا تُفتح من جديد ولا تُعرض بدائل)

| # | القرار |
|---|---|
| D1 | المواقع: **PostGIS + NetTopologySuite** (`geography(Point,4326)`) |
| D2 | الدخول: **OTP برقم الهاتف** عبر OpenIddict grant `phone_otp`، الرمز يُكتب في `SmsLog` |
| D3 | الحالات في DB كـ `string` (`HasConversion<string>()`) |
| D4 | المبالغ `long` بالدينار العراقي، بلا كسور، تقريب Half-up |
| D5 | الأدوار الثمانية: `CUSTOMER, RESTAURANT_MANAGER, RESTAURANT_KITCHEN, RESTAURANT_CASHIER, DRIVER, OPS, ACCOUNTANT, OWNER` (+ `admin` = OWNER) |
| D6 | اللغات `ar` افتراضي + `en` (و`fa`, `ur` لاحقاً) |
| D7 | مشروع `Saham.Tests` (xUnit + ABP TestBase) على قاعدة اختبار منفصلة في Neon — **بدون Docker** |

### أسلوب الكتابة (ABP Book style)
كل وحدة تمر بنفس خطوات مثال Book: Entity (منطق الحالة داخل الكيان، methods لا setters) → Enum → DbContext + `Data/Configurations` → DTOs → AutoMapper → Permissions → AppService لكل فاعل + `[Authorize]` → Localization (`ar.json`/`en.json` + رموز الأخطاء) → Migration لكل مرحلة.
قواعد إضافية: `BusinessException` + `SahamErrorCodes`، الإعدادات كـ `Setting` لا ثوابت، `ConcurrencyStamp` للقفل التفاؤلي، الأحداث المحلية داخل نفس الـ UoW، **كل وحدة تكتب جداولها فقط**، التعليقات بالإنجليزي على شكل `/* ... */` مع رموز الوثيقة (BR-xxx, REQ-xxx).

### تفضيلات المستخدم
- الكلام باللهجة العراقية البسيطة، والأسماء التقنية بالإنجليزي.
- **المستخدم يسوي الكومت بنفسه** — لا commit إلا إذا طلب.
- لا Docker.

---

## 6. طريقة الاختبار والتحقق لكل مرحلة

1. `dotnet build`
2. `dotnet ef migrations add PhaseN_<Name>` ثم build مرة ثانية
3. `dotnet run --no-build -- --migrate-database` (يطبق على Neon ويشغّل الـ Seeders)
4. تشغيل التطبيق بالخلفية: `dotnet run --no-build --launch-profile Saham` → `https://localhost:44343`
5. سكربت Python في الـ scratchpad (`urllib` للـ API، `psycopg` لفحص DB، `websockets` لـ SignalR). أمثلة من المراحل السابقة: `phase4_tests.py` … `phase7_tests.py` بالمجلد المؤقت أعلاه.
6. إيقاف التطبيق → تحديث الـ checkboxes في `docs/backend-plan.md`.

**الدخول للاختبار:**
- المالك: `admin` / `1q2w3E*` عبر password grant.
- الباقي OTP: `send-otp` ثم قراءة الرمز من `/api/app/sms-log` بحساب admin.
- بيانات تجريبية: مطاعم ومدراؤها `077000001xx/2xx/3xx`، سائقون `07700000901..903`، عمليات `07709998877`، محاسب `07709998866`، زبون `07801234567`. السائق 901 بيده 5,000 كاش + 25,000 عهدة بعد اختبارات المرحلة 7.

**الفخاخ المعروفة:**
- **Saham.exe اليتيم**: إيقاف `dotnet run` بالخلفية يترك `Saham.exe` يقفل `bin/Debug`. اقتل فقط العمليات اللي بدأت قبل دقائق (`Get-Process Saham | ? StartTime -gt ...`). إذا Saham.exe مشغّل من IDE المستخدم لا تقتله — ابنِ Release / بورت ثاني، أو اسأل.
- **OTP rate limit**: إعادة الإرسال لنفس الرقم ممنوعة 60 ث — لا تسجّل دخول نفس الرقم مرتين بسرعة.
- **Latency مع Neon**: 1–3 ث لكل طلب؛ انتظارات التوزيع/العروض ≥ 60–70 ث.
- مهل قصيرة للاختبار بمتغيرات البيئة، مثل: `env "Settings__Saham.Orders.RestaurantAutoCancelMinutes=1" dotnet run ...`.
- مطعم المشويات يفتح 12:00–02:00 بغداد؛ الصبح استخدم «بيتزا الفرات» (24 ساعة).
- سكربتات Python فيها عربي/فواصل عليا تكسر الـ heredoc — اكتبها بملف.
- لا تستخدم `sleep` بالـ foreground (المستخدم رفضه) — الانتظار داخل سكربت الاختبار.
- مسارات ABP: إذا البراميتر مو اسمه `id` ينحط اسم الأكشن أولاً (`/api/app/driver-task/pick-up/{assignmentId}`) — راجع `swagger.json`.

---

## 7. مشاكل ABP/EF اللي انحلت (لا تتراجع عنها)

| المشكلة | الحل |
|---|---|
| ABP «يلمس» الـ Aggregate لما تنضاف صفوف مرتبطة → Trigger الدفتر يرفض وتتعارض القيود المتزامنة | `AbpEntityChangeOptions.UpdateAggregateRootWhenNavigationChanges = false` (namespace `Volo.Abp.Domain.Entities.Events`) |
| جداول الدفتر لازم تكون غير قابلة للتعديل | Triggers (منع UPDATE/DELETE + فحص توازن مؤجل) بـ Migration المرحلة 7 عبر `LedgerSql`؛ `LedgerManager.PostAsync` يحفظ بـ autoSave حتى استعلام الرصيد بعده يشوف القيد |
| Records محفوظة كـ JSON (`QuoteSnapshot`...) يحاول EF يعتبرها كيانات | `builder.Ignore<T>()` **قبل** `base.OnModelCreating` |
| Entity History يفشل بتسلسل نقاط NTS | `GeoJsonConverterFactory` مضاف لـ `AbpSystemTextJsonSerializerOptions` |
| Default includes ما تشتغل بـ `Configure<AbpEntityOptions>` | `DefaultWithDetailsFunc` داخل `AddAbpDbContext(options.Entity<T>(...))` |
| SignalR مع UoW والتوكن | مسارات الـ hub مستثناة من ABP UoW middleware؛ `?access_token=` يُنسخ لـ Authorization header لـ `/signalr-hubs` |
| أحداث بعد الحالة | `AddLocalEvent` من الـ aggregates يشتغل داخل نفس المعاملة (إيقاف التوزيع عند الإلغاء، ترحيل القيود عند Delivered/DeliveryFailed)؛ الإرسال الفوري بـ `uow.OnCompleted` |
| مؤقتات الـ Workers | إعادة فحص الشرط على الصف المحمّل من جديد (Accepted→Cancelled انتقال شرعي) |
| براميتر GET اسمه `action` يرجع فارغ | اسم محجوز بـ MVC route values (يصير "GetList") — استخدم اسم ثاني (`operation`) |
| تحديث عمود «نبض» يلمس `ConcurrencyStamp` والتدقيق | `ExecuteUpdateAsync` على عمود واحد (`TabletPresenceTracker`) + `[DisableAuditing]` |
| ABP يسجل الـ enums بالتاريخ كأرقام | التحويل لأسماء وقت العرض في `AuditLogAppService.Readable` (يدعم `Nullable<>`) |

---

## 8. ملاحظات وأسئلة معلّقة

ماكو قرار معلّق للمرحلة 8. أشياء مؤجلة عن قصد:
1. **تصدير التقارير وتسجيله بالتدقيق (SRS 5.5)** ← ويا التقارير بالمرحلة 9.
2. **`FcmPushProvider` + تسجيل أجهزة المستخدمين (device tokens)** ← لحين مفاتيح Firebase.
3. **حقل اللغة المفضلة للمستخدم (DER-NTF-03)** ← حالياً كل الإشعارات بالعربي.
4. **سلسلة بصمات التدقيق** ← غير مطبقة بقرار المستخدم (خيار أ).

### أشياء منفذة بس ما انجربت فعلياً (لازم تنتبه لها)
- **`NotificationPlanner` لأحداث ما انجربت مباشرة**: `ITEM_UNAVAILABLE`, `ORDER_NOT_RECEIVED`, `CUSTOMER_CASH_BANNED`, `DRIVER_CASH_OVERDUE`, `DRIVER_REVIEWED`, `TASK_RELEASED` — مكتوبة على نفس النمط المختبر، بس ما الها اختبار خاص بعد.
- **`CashOverdueWorker` (BR-066)**: الإيقاف بسبب كاش يوم سابق ما انجرب، لأن الاختبار يحتاج قيد بتاريخ يوم سابق. الكود يشتغل كل 5 د بعد الساعة 12:00 بتوقيت بغداد.
- **السائق التجريبي `07700000901`** عليه ذمة 5,000 (فرق تسليم ناقص مقصود من الاختبار) + عهدة 25,000. إذا السيرفر شغّال بعد 12:00 من اليوم التالي راح ينوقف عن الطلبات النقدية، وهذا **تصرف صحيح**. للتصفير: سجّل تسليم 5,000 من حساب المحاسب.
- **التكرار التلقائي للطلب الثاني «نفس الاتجاه»** مطفأ حسب TD-06، والطلب الثاني يُسند يدوياً من المتابعة.
- **المكالمة داخل التطبيق** غير منفذة (ضمن الإضافات بالمرحلة 9). التواصل حالياً عن طريق المتابعة، ومحاولات الاتصال تُسجل بـ `RecordContactAttempt`.

### ذاكرة Claude
ملخص نفس السياق محفوظ أيضاً بملفات الـ memory (`saham-project-progress`, `saham-user-preferences`, `saham-testing-workflow`, `saham-abp-gotchas`) وكلها تؤشر على هذا الفولدر كمرجع أول.
