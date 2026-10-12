using System.Text;
using System.Text.Json;
using Saham.Infrastructure.Idempotency;

static byte[] Hash(string json) => RequestFingerprint.Compute(Encoding.UTF8.GetBytes(json));
static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine($"PASS: {label}");
}
Check(Hash("{\"b\":2,\"a\":1}").SequenceEqual(Hash(" { \"a\":1, \"b\":2 } ")),
    "Field order and insignificant whitespace do not change the fingerprint");
Check(!Hash("{\"amount\":1}").SequenceEqual(Hash("{\"amount\":2}")), "Changed amount conflicts");
Check(!Hash("[1,2]").SequenceEqual(Hash("[2,1]")), "Array order is significant");
Check(Hash("{\"name\":\"\\u0061\"}").SequenceEqual(Hash("{\"name\":\"a\"}")), "String escapes normalize");
Check(!Hash("1").SequenceEqual(Hash("1.0")), "Numeric representation policy is explicit");
Check(!Hash("{\"x\":null}").SequenceEqual(Hash("{}")), "Missing and null differ");
try { Hash("{\"x\":1,\"x\":2}"); throw new Exception("Duplicate fields accepted"); }
catch (JsonException) { Console.WriteLine("PASS: Duplicate fields rejected"); }
try { Hash("{"); throw new Exception("Invalid JSON accepted"); }
catch (JsonException) { Console.WriteLine("PASS: Invalid JSON rejected"); }
try { Hash(""); throw new Exception("Empty request accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: Empty request rejected"); }
