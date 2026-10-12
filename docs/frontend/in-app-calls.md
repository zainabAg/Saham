# المكالمة داخل التطبيق: دليل الربط لتطبيقات الموبايل

المكالمة صوتية بين **السائق** و**الزبون** على نفس الطلب، عن طريق **WebRTC**. السيرفر ما يرجّع رقم تلفون بأي استجابة أو حدث (TC-73). الصوت يمشي بين التطبيقين مباشرة، والسيرفر يوصل بس إشارات البداية (signalling) عن طريق SignalR.

> جهة السيرفر مكتملة ومجربة (`phase9_calls.py` 31/31). المطلوب من الموبايل: الواجهة، ومكتبة WebRTC، واتصال SignalR.

---

## 1. الاتصال بـ SignalR (إجباري للتطبيقين)

| البند | القيمة |
|---|---|
| العنوان | `wss://<server>/signalr-hubs/orders?access_token=<JWT>` |
| البروتوكول | SignalR JSON (أي مكتبة SignalR رسمية) |
| الأحداث اللي تجي للتطبيق | `callEvent` ، `callSignal` |
| الدالة اللي يستدعيها التطبيق | `SendCallSignal(callId, kind, payload)` وترجع `bool` |

**مهم:** الزبون "متصل" بس إذا اتصال SignalR مفتوح. إذا انقطع (مثلاً التطبيق بالخلفية والنظام سكّر الاتصال)، محاولة السائق ترجع `409 CustomerAppOffline`. فتطبيق الزبون لازم يرجع يتصل بـ SignalR أول ما ينفتح أو يرجع من الخلفية.

---

## 2. الـ API

كل الطلبات بـ `Authorization: Bearer <token>`.

### السائق
| الطلب | الوصف | الاستجابة |
|---|---|---|
| `POST /api/app/driver-task/call-customer/{assignmentId}` | يبدأ مكالمة. إذا اكو مكالمة شغالة لنفس الطلب يرجّعها هي (الضغطة الثانية ما تسوي مكالمة جديدة) | `CallSessionDto`، والحالة `4 = Ringing` |
| `POST /api/app/driver-task/request-ops-call/{assignmentId}` | "اطلب من المتابعة تتصل بالزبون". يحتاج هيدر **`Idempotency-Key: <uuid>`** | `204` |

### الزبون
| الطلب | الوصف | الاستجابة |
|---|---|---|
| `GET /api/app/call/active` | المكالمات الشغالة (ترن أو بيها كلام). تُستدعى عند فتح التطبيق حتى يكمل مكالمة فاتته | `{ items: CallSessionDto[] }` |
| `POST /api/app/call/{callId}/answer` | رد | `CallSessionDto` ويه `iceServers` |
| `POST /api/app/call/{callId}/decline` | رفض | `CallSessionDto` |

### الطرفين
| الطلب | الوصف |
|---|---|
| `POST /api/app/call/{callId}/end`، والـ body هو `{ "failed": false }` | إنهاء المكالمة. خلي `failed: true` إذا الصوت ما اشتغل أبداً (فشل WebRTC) |

### `CallSessionDto`
```json
{
  "callId": "guid",
  "orderId": "guid",
  "orderNumber": 1106,
  "status": 4,
  "startedAt": "2026-10-12T12:20:53Z",
  "answeredAt": null,
  "endedAt": null,
  "durationSeconds": null,
  "ringTimeoutSeconds": 45,
  "iceServers": [
    { "urls": ["stun:stun.l.google.com:19302"], "username": null, "credential": null },
    { "urls": ["turn:turn.example.com:3478?transport=udp"], "username": "1760...:abc", "credential": "base64..." }
  ]
}
```
- `iceServers` تنحط كما هي بـ `RTCPeerConnection({ iceServers })`.
- بيانات TURN **مؤقتة** (تموت بعد 30 دقيقة). لا تحفظونها، وخذوها من الاستجابة كل مرة.
- بالتطوير ممكن تجي STUN بس.

### قيم `status`
| الرقم | المعنى |
|---|---|
| `0` | Answered: انرد عليها (وبعد الإنهاء تبقى 0 ويه `durationSeconds`) |
| `1` | NoAnswer: رفض أو ما رد |
| `2` | CustomerOffline: تطبيق الزبون مو متصل |
| `3` | Failed: فشل تقني |
| `4` | Ringing: ترن |

---

## 3. أحداث `callEvent`

| `kind` | منو يستلمه | الحقول | شنو يسوي التطبيق |
|---|---|---|---|
| `incoming` | الزبون | `callId, orderId, orderNumber, driverFirstName, ringTimeoutSeconds` | يعرض شاشة الرنين: "السائق {driverFirstName}، طلب #{orderNumber}" |
| `answered` | السائق | `callId` | يبدي WebRTC (يرسل offer) |
| `declined` | السائق | `callId` | "الزبون رفض المكالمة" |
| `missed` | الطرفين | `callId` | يوكف الرنين: "ما اكو رد" |
| `ended` | الطرف الثاني | `callId, durationSeconds` | يسكّر المكالمة ويحرر المايك |

> السيرفر **يسكّر المكالمة بنفسه** ويرسل `ended` إذا انتهت مهمة السائق (تسليم، إلغاء، تحويل لسائق ثاني)، أو إذا التطبيقين انقطعوا أثناء المكالمة. الرنين ينتهي بعد `ringTimeoutSeconds` ويوصل `missed`.

## 4. إشارات WebRTC (`callSignal`)

التطبيق يرسل بـ `SendCallSignal(callId, kind, payload)`، والطرف الثاني يستلم `callSignal` بهالشكل: `{ callId, kind, payload }`.
- `kind` نص لحد 32 حرف. المتفق عليه: `offer` و `answer` و `ice`.
- `payload` أي JSON، مثلاً `{ "type": "offer", "sdp": "..." }` أو `{ "candidate": "...", "sdpMid": "0", "sdpMLineIndex": 0 }`.
- ترجع `false` إذا المرسل مو طرف بهالمكالمة، أو المكالمة انتهت.
- الإشارات مسموحة من بداية الرنين لحد الإنهاء.

---

## 5. التسلسل الكامل

```
السائق                              السيرفر                               الزبون
  │ POST call-customer ───────────────►│                                     │
  │◄── CallSessionDto (Ringing, ICE) ──│── callEvent{incoming} ─────────────►│ (يرن)
  │                                    │◄──────────── POST call/{id}/answer ─│
  │◄── callEvent{answered} ────────────│── CallSessionDto (Answered, ICE) ──►│
  │ createOffer                        │                                     │
  │ SendCallSignal(offer) ────────────►│── callSignal{offer} ───────────────►│ setRemote + createAnswer
  │◄── callSignal{answer} ─────────────│◄──────────── SendCallSignal(answer) │
  │ ⇄ SendCallSignal(ice) / callSignal{ice} بالاتجاهين                      │
  │════════════════ الصوت مباشرة بين التطبيقين (WebRTC) ════════════════════│
  │ POST call/{id}/end ───────────────►│── callEvent{ended} ────────────────►│
```

### خطوات السائق
1. يضغط "اتصال"، فيستدعي `call-customer`.
   - إذا رجع `409` بكود `Saham:CustomerAppOffline`: يعرض زر **"اطلب من المتابعة الاتصال"**، وهذا يستدعي `request-ops-call`.
2. يعرض "جاري الاتصال…" ويحفظ `callId` و `iceServers`.
3. ينتظر `callEvent`:
   - `answered`: يسوي `RTCPeerConnection({iceServers})` ويضيف المايك، وبعدها `createOffer` ويرسل `offer`.
   - `declined` أو `missed`: يسكّر الشاشة.
4. يستلم `answer`، فيسوي `setRemoteDescription`. ويتبادل `ice` بالاتجاهين.
5. "إنهاء": يستدعي `call/{id}/end`.

### خطوات الزبون
1. يبقى متصل بـ SignalR. عند فتح التطبيق يستدعي `GET call/active`.
2. يستلم `incoming`، فيعرض شاشة الرنين.
3. "رد": يستدعي `answer`، ويسوي `RTCPeerConnection` بالـ `iceServers` من الاستجابة، ويضيف المايك، وينتظر `offer`.
4. يستلم `offer`، فيسوي `setRemoteDescription` و `createAnswer` ويرسل `answer`. ويتبادل `ice`.
5. "رفض": يستدعي `decline`. و"إنهاء": يستدعي `end`.

---

## 6. الأخطاء

| HTTP | الكود | متى |
|---|---|---|
| 409 | `Saham:CustomerAppOffline` | تطبيق الزبون مو متصل. اعرض "اطلب من المتابعة" |
| 409 | `Saham:CallNotActive` | رد أو رفض أو إنهاء على مكالمة منتهية |
| 409 | `Saham:AssignmentReleased` / `Saham:NoActiveAssignment` | السائق ما عنده هالطلب حالياً، فما يكدر يتصل |
| 400 | `Saham:IdempotencyKeyRequired` | `request-ops-call` بدون هيدر `Idempotency-Key` |
| 404 | — | مكالمة مو موجودة، أو المستخدم مو طرف بيها |

نص الخطأ يجي مترجم حسب هيدر `Accept-Language` (`ar` أو `en`) بـ `error.message`.

## 7. ملاحظات للإنتاج
- بدون **TURN** أغلب المكالمات على شبكات 4G راح تفشل. السيرفر يضيف TURN تلقائياً لـ `iceServers` أول ما ينضبط قسم `Calls` (`TurnUrls` و `TurnSharedSecret`)، والتطبيق ما يحتاج أي تغيير.
- لا تعرضون أي رقم تلفون بشاشة المكالمة. الاسم الأول للسائق ورقم الطلب بس.
- إذا التطبيق رجع يتصل بـ SignalR أثناء المكالمة، يكمل عادي. السيرفر يسكّر المكالمة بس إذا **الطرفين** انقطعوا.
