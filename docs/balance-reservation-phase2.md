# المرحلة الثانية — حجز رصيد الزبون والدين

الأساس: `907439f` (بعد المرحلة الأولى للتوزيع). نواة الحساب `CustomerBalanceReservationCalculator` من حزمة Phase 2 كما هي دون تعديل.
لا تغيير في routes أو DTOs. أضيف خطآن جديدان (409) وجدول واحد وmigration واحد. لم يُنفَّذ الـmigration على أي قاعدة.

## المشكلة

`QuoteManager` كان يحسب الدين والرصيد من رصيد الدفتر الخام لكل طلب. طلبان مفتوحان لنفس الزبون يستعملان نفس الرصيد،
والدين نفسه يُجمع مرتين. الخصم الفعلي يحدث بالدفتر عند التسليم فقط.

## الحل

| الجزء | الملف |
|---|---|
| نواة الحساب (بلا تعديل) | `Saham/Entities/Customers/CustomerBalanceReservationCalculator.cs` |
| الحجز الدائم | `Saham/Entities/Customers/CustomerBalanceReservation.cs` → جدول `AppCustomerBalanceReservations` |
| القفل الموحد | `Saham/Entities/Customers/CustomerBalanceLock.cs` |
| دورة الحياة | `Saham/Entities/Customers/CustomerBalanceReservationManager.cs` |
| الربط | `QuoteManager`, `OrderManager`, `CustomerOrderAppService`, `LedgerOrderEventHandler`, `LedgerManager` |

### التخزين والقيود
- `OrderId` فريد (حجز واحد لكل طلب إلى الأبد)، ومفاتيح أجنبية إلى `AppOrders` و`AbpUsers` (Restrict).
- `CK_..._NonNegative`: لا مبالغ سالبة. `CK_..._OneKind`: لا رصيد ودين موجبان معاً. `CK_..._Finalized`: `FinalizedAt` موجود فقط خارج Active.
- فهرس جزئي `IX_AppCustomerBalanceReservations_ActiveByCustomer` على `CustomerId` حيث `Status = 'Active'` (هو ما يقرأه كل تخصيص؛ تم التحقق من SQL المولّد).
- `ConcurrencyStamp` وسجل تغييرات الكيان (ABP entity history يشمل `Saham.Entities.*`).
- الحجز ليس قيداً محاسبياً ولا إيراداً. الرصيد المؤكد يبقى من `CUSTOMER_BALANCE`؛ المزود يرجعه خاماً والكلاس وحده يطرح الحجوزات.

### القفل
- `pg_advisory_xact_lock(0x53484231, key)`؛ `key` طي ثابت لبايتات الـGuid (لا `GetHashCode`). تصادم مفتاحين يجعل زبونين ينتظران بعضهما فقط.
- يُكتسب **قبل** قراءة العرض والرصيد والحجوزات، ويبقى حتى commit/rollback. يرفض العمل خارج UoW معاملاتي.
- READ COMMITTED كافٍ: كل جملة بعد القفل ترى ما ثبّته صاحب القفل السابق. لا توجد أخطاء serialization لإعادة المحاولة.
- انتظار محدود بالإعداد `Saham.Orders.BalanceLockTimeoutSeconds` (افتراضي 30 ث، عبر `set_config('lock_timeout', …, true)` لهذه الجملة فقط) → `CustomerBalanceBusy` 409 بدل طلب معلّق. كان 10 ث ثابتاً، واختبار التكامل أظهر أن إنشاء طلب واحد من هذا الجهاز إلى Neon يستغرق ≈15 ث، فرُفض الطلب الثاني بـBusy بدل PRICE_CHANGED؛ يجب أن يتجاوز الإعداد أطول معاملة إنشاء.
- ترتيب الأقفال: الصفوف التي حدّثها المسار (طلب، سائق، إسناد) ← قفل الزبون ← الدفتر/الكوبونات/الحجوزات. الإنشاء يبدأ بالقفل ولا يلمس صفوف التسليم، فلا دورة انتظار.
- `LedgerManager.PostAsync` يرفض أي قيد يمس `CUSTOMER_BALANCE` لزبون لا يحمل قفله في المعاملة الحالية؛ أي مسار مستقبلي (تعويض، تعديل يدوي) يفشل مبكراً إن نسي القفل.
- معاملة `requiresNew` لحفظ العرض الجديد عند PRICE_CHANGED تُدخل Quote فقط ولا تطلب القفل، فلا تنتظر المعاملة الأم.

### إنشاء الطلب
1. القفل ← قراءة العرض (مستهلك؟ منتهٍ؟) ← إعادة التسعير بالبيانات الحالية (`QuoteManager` يستدعي `PlanAsync`: رصيد الدفتر الخام + الحجوزات الفعالة ← الكلاس).
2. PRICE_CHANGED إذا اختلف `PayloadHash` — وهو يشمل `DebtCollected`, `CreditApplied`, `AmountDue` — ومقارنة صريحة للمبالغ الثلاثة أيضاً.
3. أهلية الكاش من `fresh.Snapshot.CashAllowed` (الحالة الآن) لا من العرض القديم.
4. حفظ الطلب ثم `ReserveAsync` في نفس المعاملة؛ أي فشل يرجع الاثنين. المبلغان صفر ← لا حجز.
5. العرض (`POST /quotes`) معاينة فقط بلا قفل ولا حجز.

### دورة الحياة (`LedgerOrderEventHandler`، نفس معاملة القيود)
| الحدث | القيد الفعلي على CUSTOMER_BALANCE | الحجز |
|---|---|---|
| إنشاء (Placed / Scheduled / PendingPayment) | لا شيء | Active |
| تحصيل المحفظة | لا شيء (CUSTOMER_PREPAID فقط) | يبقى Active |
| Delivered (كاش/محفظة) | Dr credit, Cr debt | Consumed |
| Delivered بلا إسناد منتهٍ (لا قيد) | لا شيء | Released `DELIVERED_NOT_POSTED` |
| عدم استلام — محفظة | Dr credit, Cr debt | Consumed |
| عدم استلام — كاش | Dr T (دين جديد)، الرصيد والدين لم يُرحّلا | Released |
| حادثة سائق | لا شيء للزبون | Released |
| فشل آخر (رفض جودة/عنوان) | لا شيء (يُسوّى لاحقاً بتعديل مستقل) | Released |
| إلغاء بكل أسبابه، انتهاء/فشل الدفع، إلغاء متأخر | لا يرحّل credit/debt (الإلغاء المتأخر يرحّل S − K مستقلاً) | Released |
| تعديل مواد (BR-048) | لا شيء | يتبع مبالغ الطلب الجديدة (نزولاً فقط) |
| إعادة الحدث/Webhook مكرر | القيد بمفتاحه لا يتكرر | الانتقال نفسه لا يتكرر؛ انتقال معاكس (Consumed→Released) مرفوض |

### سياسة عدم الاتساق (لا clamp)
قيد من طلب آخر (دين عدم استلام، رسوم إلغاء متأخر، فكة مستحقة) قد يجعل الحجوزات أكبر من المرحّل أو يقلب الإشارة.
- **لا تُغيّر مبالغ الطلبات المفتوحة**: السائق يجمع والمحفظة سحبت ما وافق عليه الزبون، والدفتر يسوّي الفرق في الحساب نفسه عند ترحيلها.
- **التخصيص الجديد يفشل مغلقاً**: `CustomerBalanceReservationConflict` (409)، للعرض والإنشاء.
- **تدقيق**: بعد كل قيد على رصيد الزبون يفتح `CheckConsistencyAsync` حادثة `CustomerBalanceOvercommitted` (High) مع سجل تحذير بالمبالغ، وتُغلق تلقائياً (`CONSISTENT`) عند عودة الاتساق.
- التعديل (BR-048) يُسمح به دائماً لأنه يخفّض الحجز فقط؛ زيادة الحجز بلا مسار معتمد مرفوضة.

## الـmigration والبيانات القديمة
`20261012163500_Phase10_CustomerBalanceReservations` (جديد فقط؛ لا تعديل لترحيلات سابقة):
- ينشئ الجدول والقيود والفهارس.
- **يحوّل الطلبات المفتوحة** (ليست Delivered/Cancelled/DeliveryFailed) التي تحمل `CreditApplied` أو `DebtCollected` إلى حجوزات Active بـ`Source = LegacyBackfill` (`ON CONFLICT DO NOTHING`). الطلبات المنتهية لا تحتاج حجزاً: أثرها في الدفتر.
- يحتاج `gen_random_uuid()` (PostgreSQL 13+، Neon يدعمه).

قبل تطبيقه على قاعدة حقيقية (بالترتيب):
1. نسخة/branch من القاعدة، ثم `dotnet ef migrations script Phase9c_PushDevices Phase10_CustomerBalanceReservations` ومراجعة السكربت.
2. بعد التطبيق شغّل `CustomerBalanceReservationSql.ReconciliationQuery` (قراءة فقط): يسرد الزبائن الذين وعدت طلباتهم القديمة بأكثر من رصيدهم المرحّل (نتيجة التخصيص المزدوج قبل هذه المرحلة). هؤلاء يُرفض لهم التخصيص الجديد حتى تنتهي تلك الطلبات؛ المحاسب يقرر التسوية يدوياً. لا يوجد تصحيح آلي صامت.

## التحقق
| الاختبار | النتيجة |
|---|---|
| `dotnet build Saham.sln` | نجح، 19 تحذيراً قديماً، لا تحذير في الملفات الجديدة/المعدلة |
| `dotnet run --project test/Saham.BalanceReservation.Tests` (31 حالة من الحزمة) | 31/31. أُصلح سطر واحد لم يكن يُترجم (CS8752: `new()` مع `params`) |
| `dotnet test test/Saham.Finance.Tests` (حالات الحجز، الانتقالات، مفتاح القفل، أسماء الحالات في SQL) | 13/13 |
| `dotnet test test/Saham.Dispatch.Tests` (المرحلة الأولى) | 22/22 |
| ترجمة الاستعلامات (`ToQueryString`) وسكربت الـmigration دون اتصال | SQL صحيح، يستخدم الفهرس الجزئي |

**اختبارات التكامل على قاعدة منفصلة `saham_test`** (نفس سيرفر Neon؛ `neondb` لم تُمس) — دورتان كاملتان:
- الدورة 1: 49/49، وبعد إعادة تشغيل الخادم 4/4 (السيناريو 7).
- الدورة 2: كل ما سبق 8b نجح؛ 8b فشل بـ500 بسبب انقطاع اتصال بـNeon (timeout عند فتح الاتصال)، لا خلل في الكود؛ نجح في الدورة 1.
- استعلام التسوية يكشف زبوناً مُفرط الالتزام ويختفي بعد التصحيح.
- ملاحظات: عند الإلغاء متزامناً مع الاستلام (pickup) ظهر deadlock (40P01) بين صفوف الطلب والإسناد في مسار التوزيع، خارج قفل الرصيد؛ النتيجة المالية سليمة لكن الخاسر يأخذ 500 (مهمة منفصلة). Webhook مكرر متزامن: قيد واحد، والثاني 500 من ConcurrencyStamp (سلوك سابق).

## مخاطر معروفة
- `PaymentManager.StartAsync` يتصل بمزود المحفظة داخل معاملة الإنشاء، أي أثناء حمل قفل الزبون (كان هذا تصميم الإنشاء قبل المرحلة). يسلسل عمليات هذا الزبون فقط؛ تسليم طلب آخر له قد ينتظر حتى مهلة القفل (30 ث افتراضياً) ثم يُرفض بـ409 ويعيد التطبيق المحاولة. فصله يغيّر عقد إنشاء الطلب، فلم يُغيَّر.
- التعويضات والتعديلات اليدوية على رصيد الزبون غير منفذة بعد في المشروع (مؤجلة). عند تنفيذها: القفل ثم القيد ثم `CheckConsistencyAsync`؛ الحارس في `LedgerManager` يفرض القفل.
