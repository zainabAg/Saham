# خطة بناء الـ Backend — منصة سهم للتوصيل (ABP)

> المرجع: `Sahm-SRS-v1.3.pdf` — خصوصاً الأقسام 5 (الصلاحيات)، 10 (آلات الحالة)، 12 (المالية)، 13 (قاعدة البيانات)، 14 (المعمارية)، 15 (الـ API)، والملحق C (نطاق الـ MVP).
>
> الوثيقة تفترض NestJS، لكننا نبني نفس التصميم على **ABP 9.2 + .NET + PostgreSQL** في المشروع الحالي (Single-Layer)، بنفس نمط مثال `Book`.

---

## 1. المبادئ: كيف نطبّق نمط Book على كل شيء

كل كيان جديد يمرّ بنفس الخطوات اللي مرّ بيها `Book`:

| الخطوة | مثال Book | ما نسويه لكل وحدة |
|---|---|---|
| 1. Entity | `Entities/Books/Book.cs` (`AuditedAggregateRoot<Guid>`) | `Entities/<Module>/X.cs` — منطق الحالة **داخل الكيان** (methods مو setters عامة) |
| 2. Enum | `BookType.cs` | `Entities/<Module>/XStatus.cs` |
| 3. DbContext | `DbSet<Book>` + `builder.Entity<Book>` | `DbSet` + ملف `Data/Configurations/<Module>Configuration.cs` (extension method) حتى ما يكبر `SahamDbContext` |
| 4. DTOs | `Services/Dtos/Books/*` | `Services/Dtos/<Module>/*` |
| 5. AutoMapper | `SahamAutoMapperProfile` | نفس الملف، أو Profile لكل وحدة |
| 6. Permissions | `SahamPermissions.Books` + Provider | `SahamPermissions.<Module>` حسب مصفوفة القسم 5.3 |
| 7. AppService | `BookAppService` + `[Authorize]` | AppService لكل **فاعل** (زبون/مطعم/سائق/عمليات/مالية/إدارة) |
| 8. Localization | `ar.json` / `en.json` | مفاتيح العرض + رموز الأخطاء |
| 9. Migration | `dotnet ef migrations add` | Migration لكل مرحلة |

**إضافات ABP اللي نستخدمها فوق مثال Book** (كلها موجودة في ABP، ما نخترع شي):

| الحاجة في الوثيقة | خاصية ABP |
|---|---|
| منطق أعمال يمس أكثر من كيان (إنشاء طلب، إسناد، قيود) | `DomainService` (Managers) |
| أخطاء مثل `INVALID_STATE_TRANSITION`, `OFFER_EXPIRED` | `BusinessException` + `SahamErrorCodes` + ترجمة |
| القفل التفاؤلي (`version`) | `ConcurrencyStamp` الموجود في `AggregateRoot` |
| معاملة واحدة لكل عملية | Unit of Work (تلقائي في AppService) |
| `system_settings` (المهل، الحدود، السقوف) | `SettingDefinitionProvider` + Setting Management |
| الأدوار والصلاحيات | ABP Identity Roles + Permission Management |
| سجل التدقيق | ABP Audit Logging + Entity History |
| المهام المؤجلة والدورية (مهلة 30 ث، إقفال 04:00، إيقاف 12:00) | `BackgroundJobs` + `AsyncPeriodicBackgroundWorkerBase` |
| الأحداث داخل المعاملة | `ILocalEventBus` / Entity events |
| القناة الفورية | `Volo.Abp.AspNetCore.SignalR` |
| رفع وثائق السائق وصور الشكاوى | `BlobStoring` (Database provider موجود) |
| SMS | `Volo.Abp.Sms` (`ISmsSender`) بتنفيذ وهمي يكتب في سجل |
| بيانات تجريبية | `IDataSeedContributor` (مثل `BookStoreDataSeederContributor`) |

---

## 2. هيكل المجلدات المقترح (داخل `Saham/`)

```
Saham/
├─ Entities/
│  ├─ Geography/     City, ServiceZone, CustomerAddress
│  ├─ Restaurants/   Restaurant, RestaurantHours, MenuCategory, MenuItem, RestaurantStaff, RestaurantManager
│  ├─ Customers/     CustomerProfile
│  ├─ Pricing/       PricingPlan, PricingTier, Quote, Coupon, CouponRedemption, PricingCalculator
│  ├─ Orders/        Order, OrderItem, OrderStatusHistory, OrderModification, OrderStatus..., OrderStateMachine, OrderManager
│  ├─ Drivers/       Driver, DriverDocument, DriverLocation, DriverManager
│  ├─ Dispatch/      DeliveryOffer, DeliveryAssignment, DispatchManager
│  ├─ Payments/      Payment, PaymentEvent, Refund, PaymentManager
│  ├─ Ledger/        LedgerAccount, JournalEntry, JournalLine, CashMovement, Settlement, Adjustment, LedgerManager, PostingTemplates
│  ├─ Support/       Incident, ContactAttempt, Rating, Complaint, ComplaintAttachment
│  └─ Notifications/ NotificationMessage (outbox), SmsLog
├─ Services/
│  ├─ Customers/     RestaurantBrowseAppService, AddressAppService, QuoteAppService, CustomerOrderAppService, CustomerBalanceAppService
│  ├─ Restaurants/   RestaurantOrderAppService, MenuAppService, RestaurantProfileAppService, RestaurantStaffAppService
│  ├─ Drivers/       DriverProfileAppService, DriverOfferAppService, DriverTaskAppService, DriverLocationAppService
│  ├─ Ops/           OpsOrderAppService, IncidentAppService, DriverReviewAppService
│  ├─ Finance/       CashHandoverAppService, BalancesAppService, LedgerAppService, SettlementAppService, RefundAppService
│  ├─ Admin/         RestaurantAdminAppService, PricingPlanAppService, StaffUserAppService, CouponAppService
│  └─ Dtos/<Module>/...
├─ Data/
│  ├─ SahamDbContext.cs
│  ├─ Configurations/   GeographyConfiguration.cs, OrdersConfiguration.cs, LedgerConfiguration.cs ...
│  └─ Seeding/          RolesAndPermissionsSeeder, ReferenceDataSeeder (كربلاء، خطة التسعير، حسابات الشركة), DemoDataSeeder
├─ BackgroundWorkers/   OfferExpiryWorker, RestaurantTimeoutWorker, DriverStaleWorker, DailyCloseWorker, CashOverdueWorker, ScheduledReleaseWorker, NotificationDispatchWorker
├─ Hubs/                OrderTrackingHub, RestaurantHub, DriverHub, OpsHub
├─ Integrations/
│  ├─ Sms/              LoggingSmsSender (يكتب في SmsLog)
│  └─ Payments/         IPaymentProvider, FakeWalletProvider
├─ Identity/            PhoneOtpGrantHandler (OpenIddict extension grant), OtpChallenge, CurrentScope
├─ Infrastructure/      Idempotency (IdempotencyKey entity + filter), Money helpers, Geo (Haversine)
├─ Settings/            SahamSettings, SahamSettingDefinitionProvider
├─ Permissions/         SahamPermissions, SahamPermissionDefinitionProvider
└─ SahamErrorCodes.cs
```

> ملاحظة: نبقى على **Single-Layer** لأن المشروع مبني هكذا، والوثيقة نفسها تختار Modular Monolith. الحدود بين الوحدات = مجلدات + قاعدة: **كل وحدة تكتب جداولها فقط** (مثلاً `OrderManager` هو الوحيد اللي يغيّر `Order.Status`، و`LedgerManager` الوحيد اللي ينشئ قيود).

---

## 3. القرارات التقنية (معتمدة ✅ — لا بدائل)

| # | القرار | المعتمد |
|---|---|---|
| D1 ✅ | المواقع الجغرافية | **PostGIS + NetTopologySuite** (`geography(Point,4326)`) — «أقرب سائق» بفهرس GIST، `ST_Contains` لمناطق الخدمة، مسافة دقيقة لشرائح التسعير |
| D2 ✅ | تسجيل الدخول | **OTP برقم الهاتف** من البداية عبر OpenIddict extension grant (`grant_type=phone_otp`)، والرمز يُكتب في `SmsLog` لحين توفر مزود SMS |
| D3 ✅ | تمثيل الحالات في DB | `string` (`HasConversion<string>()`) |
| D4 ✅ | المبالغ | `long` بالدينار العراقي، بلا كسور، تقريب Half-up |
| D5 ✅ | الأدوار | أدوار ABP Identity بأسماء الوثيقة: `CUSTOMER, RESTAURANT_MANAGER, RESTAURANT_KITCHEN, RESTAURANT_CASHIER, DRIVER, OPS, ACCOUNTANT, OWNER` (+ `admin` الافتراضي = OWNER) |
| D6 ✅ | اللغات | `ar` (افتراضي)، `en`، ونضيف `fa`, `ur` لاحقاً. نحذف `fo` و`ps-PK` |
| D7 ✅ | الاختبارات | مشروع `Saham.Tests` (xUnit + ABP TestBase) لاختبارات التزامن والقيود، على **قاعدة اختبار منفصلة في Neon** (فرع/قاعدة `saham_test` بـ PostGIS) — بدون Docker |

---

## 4. نموذج البيانات بصيغة ABP

### 4.1 أنواع الكيانات
| الكيان | النوع في ABP | السبب |
|---|---|---|
| Restaurant, MenuItem, Driver, Coupon, PricingPlan, CustomerAddress | `FullAuditedAggregateRoot<Guid>` (Soft Delete = أرشفة) | الوثيقة: «أرشفة لا حذف» |
| Order, Payment, Refund, Settlement, Adjustment, Incident, Complaint | `AuditedAggregateRoot<Guid>` | لا حذف أبداً + ConcurrencyStamp |
| OrderItem, OrderStatusHistory, JournalLine, PricingTier, RestaurantHours | `Entity<Guid>` تابع لـ Aggregate | — |
| JournalEntry, CashMovement, DeliveryOffer, DeliveryAssignment | `CreationAuditedAggregateRoot<Guid>` | غير قابلة للتعديل (القيود) أو بحالة بسيطة |
| DriverLocation | `Entity<long>` | جدول كثيف الكتابة |
| CustomerProfile, Driver | مفتاحها = `UserId` (`AggregateRoot<Guid>` بـ Id = IdentityUser.Id) | 1—1 مع المستخدم |

### 4.2 الكيانات حسب الوحدة (الأعمدة الأساسية من القسم 13.4)
- **Geography**: `City(NameI18n, TimeZone, IsActive)`، `ServiceZone(CityId, ZoneType, Area, ActiveFrom/To, Status)`، `CustomerAddress(UserId, Label, Location, Description≥5, RecipientName/Phone, ArchivedAt)`.
- **Customers**: `CustomerProfile(NoShowCount, CashDisabled, CashBanLiftedBy...)` — **الرصيد والدين لا يُخزنان**، يُحسبان من الدفتر.
- **Restaurants**: `Restaurant(CityId, NameI18n, Phone, Location, Status, IsPaused, PausedReason/Until, DefaultPrepMinutes 5..120, MinOrderAmount, CommissionRateBp=1000, TabletLastSeenAt)`، `RestaurantHours(DayOfWeek, OpensAt, ClosesAt)`، `MenuCategory`، `MenuItem(Price>0, IsAvailable, IsActive)`، `RestaurantStaff(UserId, RestaurantId, Role)` لربط حسابات المطعم الثلاثة.
- **Pricing**: `PricingPlan(DriverBasePay=1500, BatchSecondOrderPay=1000, CancelAfterArrivalPayPct=50, FreeDeliveryThreshold=25000, MaxDistanceM=10000, Status)` + `PricingTier(FromM, ToM, CustomerFee, DriverExtra, FreeDeliveryEligible)` → `(0,3000]=2000`, `(3000,6000]=3000`, `(6000,10000]=4000 +1000 للسائق`. `Quote(Payload json, PayloadHash, ExpiresAt, ConsumedOrderId)`، `Coupon`، `CouponRedemption`.
- **Orders**: `Order` بكل حقول المبالغ (`ItemsSubtotal S, DiscountAmount K, DeliveryFeeBase, DeliveryFeeCharged, TotalAmount T, CommissionAmount C, RestaurantNetAmount R, DriverPayAmount P, DebtCollected, CreditApplied, AmountDue, CashTendered`) + الحالات المستقلة (`Status, HoldReason, DispatchStatus`) + الأوقات + `DeliveryOtpHash, OtpFailedAttempts, ProofMethod`. `OrderItem(NameSnapshot, UnitPrice, Quantity, LineTotal, Status)`، `OrderStatusHistory`، `OrderModification`.
- **Drivers**: `Driver(CityId, ReviewStatus, Availability, ActiveOrderCount 0..2, CashBlocked(CAP/OVERDUE), FloatAmount, LastLocation, LastLocationAt, EmergencyContactPhone)`، `DriverDocument(DocType, BlobName, Status)`، `DriverLocation`.
- **Dispatch**: `DeliveryOffer(OrderId, DriverId, RoundNo, Status, OfferedAt, ExpiresAt)`، `DeliveryAssignment(OrderId, DriverId, Status, AssignedByType, DriverPayAmount, ArrivedAtRestaurantAt, PickedUpAt, DeliveredAt, ReleasedAt, CancellationPayAmount, GeofenceFlag)`.
- **Payments**: `Payment(Method, Status, Amount, ProviderTxnId, IdempotencyKey)`، `PaymentEvent`، `Refund(Status, Attempts, NextAttemptAt)`.
- **Ledger**: `LedgerAccount(Code, AccountType, PartyType, PartyId)`، `JournalEntry(EntryType, SourceType, SourceId, IdempotencyKey UK, BusinessDate, ReversesEntryId)` + `JournalLine(AccountId, Debit, Credit, OrderId, SettlementId)`، `CashMovement`، `Settlement`، `Adjustment`.
- **Support**: `Incident(Type, Severity, OrderId, DriverId, Status, DedupeKey)`، `ContactAttempt`، (لاحقاً) `Rating`, `Complaint`.
- **Notifications**: `NotificationMessage(EventKey, Channel, UserId/Phone, Template, Payload, Status, Attempts)`، `SmsLog`.
- **Infrastructure**: `IdempotencyRecord(UserId, Scope, Key, RequestHash, Status, ResponseJson)`.

### 4.3 القيود الحرجة في قاعدة البيانات (لا تُؤجل — الملحق C §4)
تُعرّف في `Data/Configurations` عبر Fluent API، والباقي بـ `migrationBuilder.Sql(...)`:
1. **إسناد نشط واحد للطلب**: `HasIndex(a => a.OrderId).IsUnique().HasFilter("\"Status\" IN ('ASSIGNED','PICKED_UP')")`.
2. **عرض معلّق واحد للطلب وللسائق**: فهرسان فريدان جزئيان على `DeliveryOffers` بشرط `Status='PENDING'`.
3. **حد الطلبين**: `CHECK ("ActiveOrderCount" BETWEEN 0 AND 2)` على `Drivers`.
4. **دفعة فعالة واحدة**: فريد جزئي على `Payments(OrderId)`.
5. **عدم تكرار القيد**: `JournalEntries.IdempotencyKey` فريد؛ `ReversesEntryId` فريد.
6. **توازن القيد + منع التعديل/الحذف**: Trigger في Migration يرفض `UPDATE/DELETE` على `JournalLines/JournalEntries`، وفحص التوازن في `LedgerManager` (+ Trigger مؤجل اختياري).
7. **اتساق مبالغ الطلب**: `CHECK (TotalAmount = ItemsSubtotal - DiscountAmount + DeliveryFeeCharged)` وأمثالها.
8. **Coupons**: `CHECK` القيم في القسم 13.4، وحجز الاستخدام بـ `UPDATE ... WHERE used_count < limit`.

---

## 5. الصلاحيات والنطاق (القسم 5)

### 5.1 الصلاحيات
`SahamPermissions` يُبنى بنفس أسلوب `Books` لكن بمجموعات حسب الوثيقة:

```
Saham.Restaurants.Browse
Saham.Orders.Create / View / CancelCustomer / ItemDecision / AcceptReject / MarkReady
             CancelRestaurant / ItemUnavailable / HandoverToDriver / CancelOps / Modify
             ForceDeliver / MarkNotReceived
Saham.Customers.ContactView / CashBanLift
Saham.Menu.Manage / ToggleAvailability
Saham.Restaurant.HoursMinOrder / Pause / UsersManage / CouponRequest / Onboard
Saham.Drivers.DocumentsUpload / DocumentsReview / ApproveRejectSuspend
Saham.Dispatch.OfferRespond / ManualAssign
Saham.Delivery.Actions (arrive/pickup/deliver/report_failure)
Saham.Zones.Manage
Saham.Complaints.Create / Handle ;  Saham.Compensation.Credit / Approve
Saham.Refunds.ExecuteReal / RetryOrResolve
Saham.Cash.HandoverRecord / FloatIssue ;  Saham.Drivers.PayoutWeekly
Saham.Settlements.GenerateConfirm / MarkPaid / View
Saham.Ledger.AdjustmentCreate / AdjustmentApprove / Writeoff ;  Saham.Balances.View
Saham.Pricing.Manage ;  Saham.Coupons.Manage ;  Saham.Users.RoleManage
Saham.Settings.Manage ;  Saham.Reports.Operational / Financial ;  Saham.Audit.View
```

`RolesAndPermissionsSeeder` (مثل `BookStoreDataSeederContributor`) ينشئ الأدوار الثمانية ويمنحها الصلاحيات حسب المصفوفة 5.3 عبر `IPermissionDataSeeder`.

### 5.2 النطاق (◐ في المصفوفة)
الصلاحية وحدها لا تكفي: «المطعم يرى طلباته فقط» إلخ. نضيف خدمة `ICurrentScope`:
- `CurrentScope.RestaurantId` (من `RestaurantStaff` للمستخدم الحالي)، `CurrentScope.DriverId`، `CurrentScope.CustomerId`.
- كل AppService للمطعم/السائق/الزبون يفلتر بها في الاستعلام نفسه (`query.Where(o => o.RestaurantId == scope.RestaurantId)`)، والوصول لكيان خارج النطاق = `EntityNotFoundException` (لا نكشف وجوده).
- قواعد ثابتة في الكود: السائق **لا يستلم أبداً** رقم الزبون ولا رمز التسليم (DTOs منفصلة للسائق بلا هذه الحقول)، المطعم لا يرى رقم الزبون، المحاسب لا يغيّر حالة طلب (SOD-01..07).

---

## 6. المنطق الأساسي (Domain Services)

### 6.1 `PricingCalculator` (القسم 12.3)
```
T = S − K + Dfee
C = round((S − K_r) × rate)        rate = Restaurant.CommissionRateBp
R = S − K_r − C
P = 1500 + (d > 6km ? 1000 : 0)    الطلب الثاني المجمّع = 1000
Dfee = حسب الشريحة، و 0 إذا (S − K) > 25,000 والشريحة مؤهلة
amount_due = T + debt_collected − credit_applied
```
- الخادم وحده يحسب؛ التطبيق لا يرسل أي سعر.
- `QuoteAppService.CreateAsync` يحسب ويخزن `Quote` بـ hash وصلاحية دقائق؛ `CreateOrder` يعيد الحساب ويقارن.

### 6.2 `Order` + `OrderStateMachine` (القسم 10)
- جدول انتقالات واحد ثابت (T-01..T-17) داخل `OrderStateMachine`، وكل انتقال method على `Order`:
  `Accept(prepMinutes)`, `Reject(reason)`, `MarkReady()`, `MarkPickedUp(by)`, `MarkDelivered(proof)`, `Cancel(reason, by)`, `MarkFailed(reason)`, `PutOnHold(...)`, `ReleaseHold()`.
- أي انتقال غير مسموح → `BusinessException(SahamErrorCodes.InvalidStateTransition)` → HTTP 409.
- التزامن: `ConcurrencyStamp` + `AbpDbConcurrencyException` → 409.
- كل انتقال يكتب `OrderStatusHistory` ويطلق Local Event (`OrderAcceptedEto`, `OrderDeliveredEto`...) يلتقطه Dispatch/Ledger/Notifications **داخل نفس الـ UoW**.
- الثوابت INV-01..08 تُختبر آلياً.

### 6.3 `DispatchManager` (القسم 6-E، WF-03)
1. عند القبول: جدولة البحث قبل `estimated_ready_at` بـ 10 د (Background Job).
2. المؤهلون: `Availability=AVAILABLE`، `ReviewStatus=APPROVED`، موقع حديث ≤120 ث، `ActiveOrderCount < 2`، وغير موقوف عن الكاش إن كان الطلب نقدياً. الأولوية للفاضي (`ActiveOrderCount=0`).
3. ترتيب بالمسافة (PostGIS: `ST_DWithin` + `<->`) → إنشاء `DeliveryOffer` مهلته 30 ث → إرسال عبر SignalR.
4. `AcceptOffer`: في معاملة واحدة: تحقق `now ≤ ExpiresAt`، إنشاء `DeliveryAssignment`، `driver.ActiveOrderCount++` (قفل الصف `SELECT ... FOR UPDATE`)، `order.DispatchStatus=ASSIGNED`. القيد الفريد هو الضمان النهائي.
5. `OfferExpiryWorker` كل 5 ث: يحسم العروض المنتهية → السائق التالي. لا سائق لمدة 5 د → `Incident(NO_DRIVER)`.
6. إسناد يدوي من العمليات (يتجاوز العروض، مع سبب وتدقيق).

### 6.4 التسليم والإثبات
- عند الاستلام: توليد OTP من 4 أرقام، تخزين hash فقط، إرساله للمستلم عبر `ISmsSender` (سجل).
- `Deliver(code)`: 5 محاولات خاطئة ثم قفل وإحالة للمتابعة. `ForceDeliver` للعمليات بسبب إلزامي → `ProofMethod=OPS_OVERRIDE`.
- طابور السائق دون اتصال: كل إجراء يحمل `Idempotency-Key` و`ClientOccurredAt`؛ الإجراء بعد تحرير الإسناد → 409 `ASSIGNMENT_RELEASED`.

### 6.5 `LedgerManager` (القسم 12)
- `PostAsync(template, idempotencyKey, lines)`: يتحقق Σمدين = Σدائن، ويرفض إن وُجد المفتاح (يرجع القيد الموجود).
- قوالب MVP: `J-CASH-DELIVERED`, `J-WALLET-CAPTURED`, `J-WALLET-DELIVERED`, `J-DRIVER-PAY`, `J-NOT-RECEIVED`, `J-HANDOVER`, `J-FLOAT-ISSUE/RETURN`, `J-CHANGE-SHORTFALL`, `J-REFUND-INITIATED/SETTLED`, `J-REVERSAL`.
- حسابات الشركة تُنشأ في الـ Seeder؛ حسابات الطرف (سائق/مطعم/زبون) تُنشأ عند إنشائه.
- الأرصدة = `SUM(Debit − Credit)` على الحساب (استعلام، لا عمود مخزن).
- **الكاش**: `CashHandoverAppService` (المحاسب يسجل المعدود فعلياً؛ الفرق يبقى ذمة). `CashOverdueWorker` الساعة 12:00 → `CashBlocked=OVERDUE`؛ فحص السقف 300,000 عند كل تحصيل → `CAP`. الرفع آلياً عند التسليم.
- **عدم الاستلام**: انتظار 10 د + محاولة اتصال مسجلة → اعتماد العمليات → `J-NOT-RECEIVED` + `J-DRIVER-PAY`، دين على الزبون يُضاف لطلبه التالي، `NoShowCount++`، و≥2 → منع الكاش.

### 6.6 الإعدادات (`SahamSettingDefinitionProvider`)
كل قيمة ASM تُعرّف Setting بدل ثابت في الكود، مثلاً:
`Saham.Dispatch.OfferTimeoutSeconds=30`, `Saham.Dispatch.StartBeforeReadyMinutes=10`, `Saham.Orders.RestaurantAlertMinutes=2`, `Saham.Orders.RestaurantAutoCancelMinutes=10`, `Saham.Orders.ItemDecisionMinutes=5`, `Saham.Delivery.OtpMaxAttempts=5`, `Saham.Delivery.NoShowWaitMinutes=10`, `Saham.Cash.DriverCap=300000`, `Saham.Cash.OverdueHour=12`, `Saham.Cash.FloatAmount=25000`, `Saham.Compensation.OpsLimit=5000`, `Saham.Drivers.LocationFreshSeconds=120`, `Saham.Drivers.StaleMinutes=5`...

---

## 7. خطة التنفيذ بالمراحل (مطابقة لأولويات الملحق C)

> كل مرحلة تنتهي بـ: Migration + Seed + اختبار عبر Swagger + commit.

### المرحلة 0 — الأساس (يوم 1)
- [x] حزم: `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`، `Volo.Abp.AspNetCore.SignalR`، `Volo.Abp.Sms`، `Volo.Abp.BlobStoring.Database` (موجود).
- [x] `UseNpgsql(o => o.UseNetTopologySuite())` + `CREATE EXTENSION postgis` في أول Migration.
- [x] `SahamErrorCodes` + ترجمات ar/en، تنظيف اللغات (D6).
- [x] `SahamSettingDefinitionProvider` بكل المهل والحدود.
- [x] `IdempotencyRecord` + `IdempotencyActionFilter` (هيدر `Idempotency-Key`: نفس الجسم → نفس الرد، جسم مختلف → 409).
- [x] تقسيم `SahamDbContext` إلى `Data/Configurations/*`.
- [x] تفعيل Entity History للكيانات الحساسة في `AbpAuditingOptions`.

### المرحلة 1 — الهوية والأدوار والنطاق (يوم 1)
- [x] الأدوار الثمانية + كل الصلاحيات + `RolesAndPermissionsSeeder`.
- [x] `PhoneOtpGrantHandler` + `OtpChallenge` (D2) — الرمز يُكتب في `SmsLog`.
- [x] `CustomerProfile` يُنشأ تلقائياً عند أول دخول زبون.
- [x] `ICurrentScope`.
- [x] `StaffUserAppService` (المالك ينشئ موظفين)، `RestaurantStaffAppService` (المدير ينشئ مطبخ/كاشير لمطعمه فقط — SOD-05).
- [x] إضافات: `SmsLogAppService` (سجل الرسائل للوحة الإدارة)، `PhoneAuthAppService.SendOtp`، `HousekeepingWorker` (حذف مفاتيح التكرار ورموز OTP القديمة)، Migration `Phase0_1_Foundation_Identity`.

### المرحلة 2 — الكتالوج والجغرافيا (يوم 2) ✅
- [x] `City` (كربلاء seed، منطقة خدمة كمضلع اختياري)، `Restaurant`, `RestaurantHours`, `MenuCategory`, `MenuItem`؛ الأسماء `I18nText` (jsonb: ar إلزامي + en/fa/ur).
- [x] `RestaurantAdminAppService` — **CRUD على نمط BookAppService** (المالك: إنشاء، تعديل، تفعيل/إيقاف، نسبة العمولة، إنشاء حساب المدير).
- [x] `MenuAppService` (المدير: إدارة؛ المطبخ: `SetItemAvailability` فقط)، `RestaurantProfileAppService` (الدوام بما فيه بعد منتصف الليل، الحد الأدنى، وقت التجهيز، `Pause/Resume`).
- [x] `RestaurantBrowseAppService` للزبون: «يستقبل الآن» حسب الدوام والإغلاق المؤقت، و«يفتح الساعة …» للمغلق، والمسافة.
- [x] `CustomerAddress` + `AddressAppService` (وصف ≥ 5 أحرف، المستلم، داخل منطقة الخدمة). **رفض > 10 كم يُفحص عند عرض السعر (المرحلة 3)** لأنه يعتمد على المطعم.
- [x] `CityAppService` (رسم منطقة الخدمة للمالك)، `RestaurantStaffManager` مشترك، `ICurrentScope.ResolveRestaurantIdAsync` (المالك/العمليات يمررون `restaurantId`).
- [x] حذف مثال `Book`، Migration `Phase2_Catalog_Geography`، بيانات تجريبية (3 مطاعم بحسابات).

### المرحلة 3 — التسعير وعرض السعر (يوم 2) ✅
- [x] `PricingPlan` + `PricingTier` + Seed بالقيم المعتمدة؛ `PricingPlanAppService` للمالك: مسودة (أو نسخة من النشطة) ← تعديل ← تفعيل، والخطة النشطة لا تُعدّل، وخطة نشطة واحدة لكل مدينة (فهرس جزئي فريد).
- [x] `PricingCalculator` — مطابق لأمثلة القسم 12.6 (S=23,000، 4.2 كم ← رسوم 3,000، T=26,000، C=2,300، R=20,700، P=1,500).
- [x] `QuoteAppService.CreateAsync` + `QuoteManager`: الدوام/الإغلاق، صلاحية العنوان، ≤ 10 كم، توفر المواد، الحد الأدنى، المجاني و«أضف X للمجاني»، الدين والرصيد (`ICustomerBalanceProvider` = صفر لحين الدفتر في المرحلة 7)، منع الكاش، الوقت المتوقع كمدى، والطلب المجدول (60 د – 3 أيام ضمن الدوام).
- [x] لقطة العرض (`QuoteSnapshot` jsonb + hash) لاستخدامها عند إنشاء الطلب في المرحلة 4 (`PRICE_CHANGED`).
- [ ] الكوبونات مؤجلة لإضافات المرحلة 9 (الحقول موجودة و K = 0).

### المرحلة 4 — الطلب ودورة حياته عند المطعم (يوم 3)
- [ ] `Order` + `OrderItem` + `OrderStatusHistory` + `OrderStateMachine` + `OrderManager`.
- [ ] `CustomerOrderAppService.CreateAsync` (نقدي فوري؛ إلزامي Idempotency؛ تصريح الورقة `CashTendered`؛ منع الكاش للممنوع).
- [ ] `RestaurantOrderAppService`: الطلبات الحالية، `Accept(prep 5..120)`, `Reject(reason)`, `MarkReady`, `ReportItemUnavailable`, `CancelAfterAccept(reason)`, `HandoverToDriver` (كاشير).
- [ ] نفاد المادة (الأساسي): `HoldReason=ITEM_UNAVAILABLE` → قرار الزبون «أكمل بدونها / ألغِ» خلال 5 د → وإلا إكمال آلي + تعديل المبالغ.
- [ ] الإلغاءات: الزبون قبل القبول، رفض المطعم، المطعم بعد القبول، العمليات، والإلغاء الآلي بعد 10 د عدم رد (`RestaurantTimeoutWorker`).
- [ ] `RestaurantHub` (SignalR) لتنبيه الطلب الجديد.

### المرحلة 5 — السائق والتوزيع (يوم 3–4)
- [ ] `Driver` + `DriverDocument` (BlobStoring) + `DriverProfileAppService` (تسجيل، رفع وثائق، `SetAvailability`).
- [ ] `DriverReviewAppService`: العمليات تقبل/ترفض الوثائق، **المالك وحده** يوافق نهائياً (SOD-07).
- [ ] `DriverLocationAppService.PostAsync` (دفعات مواقع) → `LastLocation`.
- [ ] `DispatchManager` + `DeliveryOffer` + `DeliveryAssignment` + `OfferExpiryWorker` + `DriverHub`.
- [ ] `DriverOfferAppService`: `GetCurrent`, `Accept`, `Decline` (العرض يعرض أجره والباقي المطلوب للفكة).
- [ ] `OpsOrderAppService.AssignManually` + `Incident(NO_DRIVER)`.

### المرحلة 6 — الاستلام والتسليم (يوم 4)
- [ ] `DriverTaskAppService`: `ArrivedAtRestaurant` (ضمن 200 م)، `PickUp`, `ArrivedAtCustomer`, `Deliver(otp)`, `ReportFailure`, `ReportChangeShortfall`.
- [ ] OTP + `SmsLog` + `OrderTrackingHub` للزبون (حالة + موقع السائق).
- [ ] قبول `ClientOccurredAt` بالمنطق المذكور في EX-01.
- [ ] `OpsOrderAppService.ForceDeliver(reason)` + `DriverStaleWorker` (حادثة بعد 5 د بلا موقع).

### المرحلة 7 — المال (يوم 5) — **نقطة القرار: رحلة كاملة حتى تحديث الأرصدة**
- [ ] `LedgerAccount`, `JournalEntry`, `JournalLine` + Triggers + `LedgerManager` + قوالب القيود.
- [ ] معالجات الأحداث: `OrderDelivered → J-CASH-DELIVERED + J-DRIVER-PAY` إلخ.
- [ ] `BalancesAppService`: ذمة كل سائق، رصيد كل مطعم، رصيد/دين الزبون (`/me/balance`).
- [ ] `CashHandoverAppService` + `CashMovement` + `CashOverdueWorker` + فحص السقف.
- [ ] «لم يُستلم»: `ContactAttempt` + `OpsOrderAppService.MarkNotReceived` + الدين + منع الكاش بعد مرتين + `LiftCashBan` للمالك.
- [ ] ملخص السائق اليومي (`/driver/summary/today`).

### المرحلة 8 — لوحة العمليات والإدارة والتدقيق (يوم 5)
- [ ] `OpsOrderAppService.GetLive`, `GetDriversMap`، `IncidentAppService` (فتح/إقرار/حل، `DedupeKey`)، `OpsHub`.
- [ ] عرض سجل التدقيق (`Saham.Audit.View`) فوق جداول ABP Audit Logging، مع `reason` لكل فعل ✱.
- [ ] `NotificationDispatchWorker` (Outbox → SignalR/Push/SMS log مع إعادة المحاولة).

### المرحلة 9 — الإضافات (بالترتيب، إن بقي وقت)
1. الدفع بالمحفظة + `FakeWalletProvider` + webhook + الاسترداد الآلي (`RefundWorker`).
2. الكشوف الرسمية: يومي للسائق، أرباح الخميس، أسبوعي للمطعم (`Settlement` + `DailyCloseWorker` 04:00).
3. البديل في نفاد المادة واتصال المتابعة.
4. الكوبونات بنوعيها + طلب كوبون المطعم وموافقة المالك.
5. أجر السائق عند الإلغاء على المتسبب (نصف/كامل).
6. التقارير الشهرية + تصدير Excel (`MiniExcel` أو `ClosedXML`).
7. الطلب المجدول (حتى 3 أيام) + `ScheduledReleaseWorker`.

### المرحلة 10 — الاختبارات (يوم 6)
- [ ] TC-14: 50 قبول متزامن → إسناد واحد. TC-15: لا يتجاوز سائق طلبين.
- [ ] كل الأزواج غير المسموحة في جدول الانتقالات (TC-49).
- [ ] تكرار الطلب بنفس المفتاح (TC-35..37). توازن الدفتر ومطابقة الأرصدة (TC-30).
- [ ] اختبارات النطاق: مطبخ لا يغير الأسعار، سائق لا يرى الرمز ولا الرقم.

### بعد الـ MVP (المرحلة 2 في الوثيقة)
الشكاوى بالصور والتعويض، الطلب من مطعمين (`CheckoutGroup`)، المناطق المغلقة وإيقاف الاستقبال، واجهة القيود التصحيحية، وضع الاستعادة، مطابقة موقع التسليم، المسافة بالطريق، مزودا SMS ومحفظة حقيقيان.

---

## 8. ما يُحذف/يُنظف
- ~~مثال `Book`~~ — حُذف في المرحلة 2 مع جدوله.
- اللغات `fo` و `ps-PK`.

---

## 9. أول خطوة عند بدء التنفيذ
القرارات D1–D7 معتمدة؛ نبدأ بالمرحلة 0 + 1.
