# الخطوات الجاية — المرحلة التاسعة (الإضافات)

> اقرأ أولاً `SESSION-HANDOFF.md`. المرحلة 8 منجزة ومختبرة على Neon وتنتظر كومت المستخدم.
> بعد كل خطوة: `dotnet build`. بنهاية كل بند: Migration، ثم اختبار بسكربت، ثم تحديث `docs/backend-plan.md`.
> **لا commit** — المستخدم يسويه بنفسه.

## 0. قبل البدء
- [ ] تأكد إن المستخدم سوّى كومت المرحلة 8 (`git log -1`)؛ إذا لا، اسأله قبل أي تعديل جديد.
- [ ] المرحلة 9 = إضافات «إن بقي وقت» بالترتيب أدناه (من `docs/backend-plan.md`)؛ اسأل المستخدم أي بنود يريدها وبأي ترتيب.

## 1. البنود (ترتيب الخطة)
1. الدفع بالمحفظة + `FakeWalletProvider` + webhook + الاسترداد الآلي (`RefundWorker`)، قيود `J-WALLET-*`. أحداث الإشعار: `REFUND_SUCCEEDED`, `REFUND_FAILED_FINAL` (أضفها لـ `OutboxEventTypes` و`NotificationPlanner`)، وحادثة `REFUND_FAILED` للمحاسب (أضفها لـ `Incident.FinancialTypes`).
2. الكشوف الرسمية: يومي للسائق، أرباح الخميس، أسبوعي للمطعم (`Settlement` + `DailyCloseWorker` 04:00). إشعار `DRIVER_PAYOUT_PAID`.
3. البديل في نفاد المادة واتصال المتابعة.
4. الكوبونات بنوعيها + طلب كوبون المطعم وموافقة المالك (إشعار `RESTAURANT_COUPON_REQUESTED` للمالك كحادثة).
5. أجر السائق عند الإلغاء على المتسبب (نصف/كامل).
6. التقارير الشهرية + تصدير Excel — **وسجّل كل تصدير بالتدقيق (SRS 5.5)**، مؤجل من المرحلة 8.
7. الطلب المجدول (حتى 3 أيام) + `ScheduledReleaseWorker`.

## 2. أشياء مفتوحة من المرحلة 8
- [ ] `FcmPushProvider` + جدول أجهزة المستخدمين (device tokens) لما تتوفر مفاتيح Firebase — يستبدل `LoggingPushSender`.
- [ ] حقل اللغة المفضلة للمستخدم، و`NotificationSender` يرندر بلغته بدل `ar` الثابتة.
- [ ] اختبارات لأحداث الإشعار اللي ما انجربت مباشرة: `ITEM_UNAVAILABLE`, `ORDER_NOT_RECEIVED`, `CUSTOMER_CASH_BANNED`, `DRIVER_CASH_OVERDUE`, `DRIVER_REVIEWED`, `TASK_RELEASED` (ممكن ضمن المرحلة 10).
