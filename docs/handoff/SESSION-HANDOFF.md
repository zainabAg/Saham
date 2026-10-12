# تسليم الجلسة — مشروع سهم (Saham)

> آخر تحديث: 2026-10-11 (بعد إنجاز المرحلة 9). اقرأ هذا الملف + `NEXT-STEPS.md` + `docs/backend-plan.md` قبل أي شغل بجلسة جديدة.

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
| 7 | المال (`LedgerManager`, القيود، Triggers، `CashManager`, الأرصدة، «لم يُستلم») | `baf5122` |
| 8 | لوحة العمليات، الحوادث، سجل التدقيق (فوق جداول ABP)، الـ Outbox والإشعارات | `93395ed` |
| 9 | المحفظة والاسترداد، الكشوف، البديل، الكوبونات، الإلغاء على المتسبب، التقارير + Excel، الإرسال المجدول | **منجزة ومختبرة — بانتظار كومت المستخدم** |

الفرع الحالي: `Develop` (الفرع الرئيسي `main`). Migrations كلها مطبقة على Neon (آخرها `Phase9_Payments_Coupons_Settlements`). آخر كومت: `93395ed` (المرحلة 8).

البنود المؤجلة صراحةً من المراحل السابقة (موجودة كـ `[ ]` في الخطة):
- الكوبونات ← المرحلة 9.
- الدفع بالمحفظة `J-WALLET-*` والاسترداد، الكشوف الرسمية ودفع الخميس ← المرحلة 9.

---

## 3. المرحلة التاسعة — منجزة (بانتظار الكومت)

### شنو انبنى
| البند | الملفات الأساسية |
|---|---|
| المحفظة (UC-05) | `Entities/Payments/` (`Payment`, `PaymentEvent`, `Refund`, `PaymentManager`)، `Integrations/Wallet/` (`IWalletProvider`, `FakeWalletProvider`)، `Controllers/PaymentWebhookController.cs` (`POST /api/payments/webhook/fake`، توقيع HMAC بالـ header `X-Saham-Signature`)، `Controllers/FakeWalletController.cs` (صفحة دفع وهمية، Development فقط)، `PaymentTimeoutWorker` |
| الاسترداد (UC-22) | `RefundWorker`، `Services/Finance/RefundAppService.cs` (إعادة المحاولة؛ الإرجاع اليدوي **للمحاسب حصراً** بفحص الدور)، `PaymentPostingService` (`J-WALLET-*`, `J-REFUND-*`) |
| الأموال عند تغيّر الطلب | `LedgerOrderEventHandler` صار يغطي: التسليم (كاش/محفظة)، «لم يُستلم»، حادثة السائق، **الإلغاء** (`J-CANCEL-DRIVER-PAY`, `J-CUSTOMER-LATE-CANCEL`، الاسترداد، الكوبون)، وتعديل المواد (استرداد الفرق) |
| الكشوف | `Entities/Settlements/` (`Settlement`, `SettlementManager`)، `DailyCloseWorker`، `Services/Finance/SettlementAppService.cs`، Trigger `trg_settlements_frozen` |
| البديل واتصال المتابعة | `Order.ReportItemsUnavailable(..., substitutes)`، `ItemDecision.AcceptSubstitute`، `Order.EscalateItemDecision`، `OrderTimeoutsWorker` (حادثة ثم دقائق إضافية ثم الإكمال) |
| الكوبونات | `Entities/Coupons/`، `Services/Admin/CouponAppService.cs` (المالك)، `Services/Restaurants/RestaurantCouponAppService.cs` (طلب المطعم)، الكوبون بـ `QuoteManager` و`OrderManager` |
| الإلغاء على المتسبب | `CancelCause` + `OpsCancelOrderDto.Cause` (Company / Restaurant / Customer)، `CancelReason.CustomerLateCancel` |
| التقارير | `Services/Reports/ReportAppService.cs` (`GET /api/app/report?report=N&from=&to=`, `POST /api/app/report/export` → xlsx)، حزمة `MiniExcel` |
| المجدول | `ScheduledReleaseWorker` + `OrderSweeper` (الحلقة المشتركة لمؤقتات الطلب) |
| المكالمة داخل التطبيق (البند 8) | `Entities/Support/VoiceCallManager.cs`، `ContactAttempt` (+ `AnsweredAt`, `EndedAt`, `DurationSeconds`, نتيجة `Ringing`)، `Services/Calls/CallAppService.cs`، `DriverTask/CallCustomer` + `RequestOpsCall`، `OrdersHub.SendCallSignal` (`callSignal`) + `CallNotifier` (`callEvent`)، `Hubs/UserPresenceTracker.cs`، `Integrations/Calls/IceServerProvider.cs` (قسم `Calls` بالإعدادات: `StunUrls`, `TurnUrls`, `TurnSharedSecret`)، `CallTimeoutWorker`، Migration `Phase9b_InAppCalls` (تعبئة `EndedAt` للمحاولات القديمة) |
| إعدادات جديدة | `Saham.Payments.WalletEnabled`، `Saham.Payments.RefundRetrySeconds` (`60,300,900,3600,10800`)، `Saham.Settlements.DriverPayoutDay` (Thursday)، `Saham.Settlements.RestaurantWeekStartDay` (Saturday)، `Saham.Orders.ItemDecisionOpsMinutes` (5) |
| إعدادات تطبيق (appsettings) | قسم `Wallet:Fake` (`WebhookSecret`, `PayPageBaseUrl`, `EnablePayPage`, `RefundFailure` = None/Retryable/Final للاختبار) |

### ملاحظات تصميم مهمة
- المال المقبوض بالمحفظة يبقى `CUSTOMER_PREPAID` لحد ما ينتهي الطلب. كل سطر مسبق يحمل `OrderId`، فـ«المتبقي للطلب» = مجموع واحد، والاسترداد عند الإلغاء = هذا المتبقي بعد خصم رسوم الإلغاء.
- `BR-042` (الاسترداد ≤ المقبوض) محمي بـ `Payment.RefundedAmount` + الـ ConcurrencyStamp + CHECK بقاعدة البيانات.
- حجز الكوبون ذري بـ `ExecuteUpdate ... WHERE UsedCount < Limit`، والتحرير يرجع الاستخدام.
- الكشف يربط أسطر حساب الطرف غير المسوّاة لحد نهاية الفترة (`SettlementId` NULL → قيمة، التحديث الوحيد المسموح بـ Trigger الدفتر)، وسطر الدفع ينضاف لنفس الكشف.
- حساب `admin` بـ ABP عنده **كل** الصلاحيات تلقائياً، فالفصل بين المهام (SOD) يحتاج فحص دور صريح، مثل `ResolveManuallyAsync`.

### نتيجة التحقق على Neon
- `phase9_tests.py` (تشغيل A): **75/75** — الكوبونات، المحفظة (توقيع خاطئ، مكرر، مبلغ خاطئ، قبض متأخر، انتهاء، استعلام صامت)، الاسترداد الناجح والإشعار، البديل واسترداد الفرق، الإلغاء المتأخر (محفظة وكاش ودين يُحصّل لاحقاً)، أجر السائق عند الإلغاء، التصعيد، الإرسال المجدول والإلغاء عند الإغلاق، كل التقارير والنطاقات والتصدير والتدقيق.
- `phase9_refund_failure.py` (تشغيل B، `Wallet__Fake__RefundFailure=Retryable`): **13/13**.
- `phase9_calls.py` (`Calls.RingTimeoutSeconds=10`، اتصالا SignalR حقيقيان للزبون والسائق + متطفل): **26/26** — بلا رقم بأي استجابة أو حدث، الإشارة للطرف الآخر فقط، رد/رفض/إنهاء/انتهاء الرنين، تطبيق مغلق ← 409، لا مكالمة بعد انتهاء المهمة. وأعيد `phase9_tests.py` كاملاً بعدها (77 فحص، كلها ناجحة).
- `phase9_settlements.py` (تشغيل C، `Cash.BusinessDayEndHour=-164` حتى «اليوم» يصير 10-18 والأسبوع مغلق، و`Cash.OverdueHour=24`): **26/26** (اثنين منها انعادوا بعد تصليح حساب نطاق التقرير).
- ⚠️ أثر تشغيل C على قاعدة التطوير: قيدا الدفع (`payout:…` و`driver_payout:…`) بتاريخ تشغيلي **2026-10-18**، ورصيد «بيتزا الفرات» وأرباح السائق 902 صاروا صفر (مدفوعة)، وبقيت كشوف Draft للسائق 901.
- السكربتات بالمجلد المؤقت: `C:\Users\BESTLA~1\AppData\Local\Temp\claude\E--abp-projects-Saham\ff7f1f2e-ea8f-4495-acf3-4807969264ac\scratchpad\` (`phase8_tests.py` و`phase9_*.py`).

---

## 4. حالة git
- آخر كومت `93395ed` (المرحلة 8) على `Develop`، ومرفوع لـ `origin/Develop`.
- `main` المحلي متقدّم لـ `93395ed` (fast-forward)، بس **ما انرفع** لـ GitHub لأن حساب `Mtaki4` المحفوظ بالجهاز ما عنده صلاحية كتابة على `zainabAg/Saham` (403). المستخدم يرفعه بحسابه.
- كل شغل المرحلة 9 **غير متكومت**، ومعه Migration `20261011122631_Phase9_Payments_Coupons_Settlements` (مطبقة على Neon).
- `.gitignore` (سطر `appsettings.secrets.json`) بعده **ما متكومت** — مهم يتكومت.

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
| قراءة جسم الطلب يدوياً (webhook) تكسر الاستجابة: "Cannot access a closed Stream" | `new StreamReader(Request.Body, …, leaveOpen: true)` |
| webhook مزوّر يحجز معرّف الحدث الحقيقي فيُعامل الحقيقي كمكرر | الحدث بتوقيع خاطئ ينحفظ بمعرّف `invalid_…` جديد |
| دور `admin` بـ ABP عنده كل الصلاحيات (ومنها الجديدة) | الفصل بين المهام بفحص دور صريح (`IsInRole(Accountant)`) |
| `OrderType` موجود بـ `Saham.Entities.Pricing` مو `Orders` | استعمل الـ namespace الصح |
| تشغيل اختبار ينقطع بالنص يخلي «بيتزا الفرات» موقوفاً مؤقتاً | تأكد من `IsPaused` قبل إعادة الاختبار، أو استأنف المطعم |

---

## 8. ملاحظات وأسئلة معلّقة

ماكو قرار معلّق. مؤجل عن قصد (خارج المرحلة 9 أو ينتظر طرف خارجي):
1. **مزود محفظة حقيقي (EXT-01)**: يستبدل `FakeWalletProvider` بتطبيق `IWalletProvider`.
2. **`FcmPushProvider` + أجهزة المستخدمين**: لحين مفاتيح Firebase.
3. **اللغة المفضلة للمستخدم (DER-NTF-03)**: الإشعارات كلها عربي حالياً.
4. **القيود التصحيحية (UC-25) ومصاريف التشغيل الشهرية (J-OPERATING-EXPENSE)**: ما انبنت، فـ RPT-04 يعلّم صافي الربح «غير مكتمل».
5. **اعتماد الإرجاع اليدوي من شخص ثانٍ (BR-056 / SOD-01)**: حالياً المحاسب يسويه مباشرة مع مرجع.
6. **الشكاوى والتقييمات وRPT-10، المناطق المغلقة، تغيير عنوان التسليم** (بعد MVP حسب الملحق C §3). المكالمة داخل التطبيق **انبنت** (يبقى تشغيل خادم coturn فعلي للإنتاج).
8. **حضور المستخدمين (`UserPresenceTracker`) بالذاكرة**: يصلح لخادم واحد؛ عدة خوادم تحتاج Redis + SignalR backplane.
7. **«تحصيل من المطعم» (صافي سالب)**: مكتوب (`MarkCollectedAsync`) بس ما انجرب لأن ماكو مطعم صافيه سالب ببيانات الاختبار.

### أشياء منفذة بس ما انجربت فعلياً (لازم تنتبه لها)
- **`NotificationPlanner` لأحداث ما انجربت مباشرة**: `ITEM_UNAVAILABLE`, `ORDER_NOT_RECEIVED`, `CUSTOMER_CASH_BANNED`, `DRIVER_CASH_OVERDUE`, `DRIVER_REVIEWED`, `TASK_RELEASED` — مكتوبة على نفس النمط المختبر، بس ما الها اختبار خاص بعد.
- **`CashOverdueWorker` (BR-066)**: الإيقاف بسبب كاش يوم سابق ما انجرب، لأن الاختبار يحتاج قيد بتاريخ يوم سابق. الكود يشتغل كل 5 د بعد الساعة 12:00 بتوقيت بغداد.
- **السائق التجريبي `07700000901`** عليه ذمة 5,000 (فرق تسليم ناقص مقصود من الاختبار) + عهدة 25,000. إذا السيرفر شغّال بعد 12:00 من اليوم التالي راح ينوقف عن الطلبات النقدية، وهذا **تصرف صحيح**. للتصفير: سجّل تسليم 5,000 من حساب المحاسب.
- **التكرار التلقائي للطلب الثاني «نفس الاتجاه»** مطفأ حسب TD-06، والطلب الثاني يُسند يدوياً من المتابعة.
- **المكالمة داخل التطبيق** منفذة بالكامل من جهة السيرفر (المرحلة 9b) ومجربة: `phase9_calls.py` **31/31** (2026-10-12)، ومنها إغلاق المكالمة العالقة تلقائياً (`VoiceCallManager.CloseStaleAsync` عن طريق `CallTimeoutWorker`) إذا انتهت مهمة السائق أو انقطع التطبيقان أثناء المكالمة. الناقص للإنتاج: خادم TURN (coturn) فعلي وقسم `Calls` بالإعدادات، وواجهة المكالمة بتطبيقات الموبايل. دليل الربط للفرونت: `docs/frontend/in-app-calls.md`.

### ذاكرة Claude
ملخص نفس السياق محفوظ أيضاً بملفات الـ memory (`saham-project-progress`, `saham-user-preferences`, `saham-testing-workflow`, `saham-abp-gotchas`) وكلها تؤشر على هذا الفولدر كمرجع أول.
