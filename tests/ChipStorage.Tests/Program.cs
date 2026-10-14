using AtomixAI.Core;
using Newtonsoft.Json.Linq;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Reject(ChipStorage storage, JArray? ids)
{
    try { storage.CommitAndCreateSnapshot(ids); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid or stale chip IDs were accepted.");
}

var storage = new ChipStorage();
var data = JObject.Parse("{\"nested\":{\"value\":12,\"units\":\"mm\"}}");
Check(storage.GetApprovedEntries().Count == 0, "Opening the parameter menu must not create approved chips.");
var first = storage.CreatePending("#Height", data);
var abandoned = storage.CreatePending("#Width", new JObject());
Check(storage.GetApprovedEntries().Count == 0, "Newly selected chips must remain pending.");
data["nested"]!["value"] = 99;
Reject(storage, new JArray(first, "chip_999999"));
Check(storage.GetApprovedEntries().Count == 0, "Invalid submissions must not partially approve chips.");

var snapshot = storage.CommitAndCreateSnapshot(new JArray(first, first));
Check(snapshot.Count == 1, "Duplicate IDs must resolve only once.");
Check(snapshot[0]["data"]!["nested"]!["value"]!.Value<int>() == 12, "Pending chip creation must clone payloads.");
var approved = storage.GetApprovedEntries();
Check(approved.Count == 1, "Only chips submitted with the message must be approved.");
Check(approved[0]["id"]!.Value<string>() == first, "Approval must preserve the pending chip ID.");
Check(snapshot[0]["id"]!.Value<string>() == first, "The snapshot must contain only the submitted chip.");
snapshot[0]["data"]!["nested"]!["value"] = 88;
var independent = storage.CommitAndCreateSnapshot(new JArray(first));
Check(independent[0]["data"]!["nested"]!["value"]!.Value<int>() == 12, "Snapshots must not mutate storage.");
Check(storage.GetApprovedEntries().Count == 1, "Model Data entries must exclude discarded pending chips.");
Reject(storage, new JArray(abandoned));
Reject(storage, new JArray("chip_999999"));
Reject(storage, new JArray(12));
Reject(storage, new JArray(""));
Reject(storage, null);

var otherStorage = new ChipStorage();
Check(otherStorage.CreatePending("#Other", new JObject()) == "chip_1", "Each storage instance must own its ID sequence.");
var afterClear = storage.CreatePending("#Height", data);
Check(long.Parse(afterClear.Substring(5)) > long.Parse(abandoned.Substring(5)), "Discarded pending IDs must not be reused.");
storage.Clear();
Reject(storage, new JArray(first));
var afterStorageClear = storage.CreatePending("#Height", data);
Check(long.Parse(afterStorageClear.Substring(5)) > long.Parse(afterClear.Substring(5)), "Clear must not reuse IDs.");
Check(independent[0]["data"]!["nested"]!["value"]!.Value<int>() == 12, "Clear must not change request snapshots.");
var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
Parallel.For(0, 100, _ => ids.Add(storage.CreatePending("same label", new JObject())));
Check(ids.Distinct().Count() == 100, "Concurrent registration must allocate unique IDs.");
Console.WriteLine("ChipStorage tests passed.");
