using AIVTuber.Web.Services;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

var preparing = new IdleActivityService();
var cancelled = preparing.TryBegin(TimeSpan.Zero)!;
preparing.Begin();
Check(cancelled.IsCancellationRequested, "Chat cancels unpublished idle preparation");
Check(!preparing.Publish(() => throw new Exception("Must not publish")), "Cancelled preparation cannot publish");
preparing.Finish();
preparing.End();

var activity = new IdleActivityService();
var idle = activity.TryBegin(TimeSpan.Zero)!;
var turns = new SpeechTurnCoordinator();
await turns.WaitAsync();
int segments = 0;
Check(activity.Publish(() => segments++), "First audio commits idle response");
activity.Begin();
Check(!idle.IsCancellationRequested, "Chat does not cancel committed idle response");
var waitingChat = turns.WaitAsync();
Check(!waitingChat.IsCompleted, "Chat waits while idle owns speech turn");
Check(activity.Publish(() => segments++), "Second segment survives new chat");
Check(activity.Publish(() => segments++), "Last segment survives new chat");
Check(segments == 3 && !waitingChat.IsCompleted, "All chunks publish before chat handoff");
activity.Finish();
turns.Release();
await waitingChat.WaitAsync(TimeSpan.FromSeconds(1));
Check(activity.TryBegin(TimeSpan.Zero) == null, "Queued chat prevents next idle paragraph");
activity.End();
turns.Release();
Check(activity.TryBegin(TimeSpan.Zero) != null, "Idle can resume after chat finishes");
activity.Finish();

for (int i = 0; i < 100; i++)
{
    var race = new IdleActivityService();
    var token = race.TryBegin(TimeSpan.Zero)!;
    bool committed = false;
    await Task.WhenAll(Task.Run(() => committed = race.Publish(() => { })), Task.Run(race.Begin));
    if (committed == token.IsCancellationRequested) throw new Exception("Commit/cancel race broke speech boundary");
    race.Finish();
    race.End();
}
Console.WriteLine("PASS: 100 concurrent chat/idle commitment races");

var priority = new SpeechTurnCoordinator();
await priority.WaitAsync(SpeechSource.Idle);
var gameTurn = priority.WaitAsync(SpeechSource.Game);
var liveTurn = priority.WaitAsync(SpeechSource.LiveChat);
var directTurn = priority.WaitAsync(SpeechSource.DirectChat);
priority.Release();
await directTurn.WaitAsync(TimeSpan.FromSeconds(1));
Check(!liveTurn.IsCompleted && !gameTurn.IsCompleted, "Direct chat leads waiting speech");
priority.Release();
await liveTurn.WaitAsync(TimeSpan.FromSeconds(1));
Check(!gameTurn.IsCompleted, "Live chat leads waiting game speech");
priority.Release();
await gameTurn.WaitAsync(TimeSpan.FromSeconds(1));
priority.Release();

await priority.WaitAsync(SpeechSource.Idle);
using var cancelledWait = new CancellationTokenSource();
var skipped = priority.WaitAsync(SpeechSource.DirectChat, cancelledWait.Token);
var surviving = priority.WaitAsync(SpeechSource.Game);
cancelledWait.Cancel();
try { await skipped; throw new Exception("Cancelled turn acquired speech"); }
catch (OperationCanceledException) { }
priority.Release();
await surviving.WaitAsync(TimeSpan.FromSeconds(1));
priority.Release();
Check(true, "Cancelled high-priority turn does not block the next speaker");

await priority.WaitAsync(SpeechSource.Game);
CancellationToken activeGame = priority.ActiveCancellationToken;
var nextTurn = priority.WaitAsync(SpeechSource.DirectChat);
priority.InterruptCurrent();
Check(activeGame.IsCancellationRequested, "Explicit interrupt cancels the active speaker");
priority.Release();
await nextTurn.WaitAsync(TimeSpan.FromSeconds(1));
Check(!priority.ActiveCancellationToken.IsCancellationRequested,
    "The next speaker gets a fresh interruption token");
priority.Release();

var memory = new IdleMonologueMemory();
string first = memory.NextDirection().Split('\n')[0];
memory.Remember("레몬 타르트는 바삭한 식감과 새콤한 크림이 잘 어울려.");
Check(memory.IsRepetitive("레몬 타르트는 바삭한 식감과 새콤한 크림이 잘 어울려!"), "Punctuation changes do not bypass repetition filter");
Check(!memory.IsRepetitive("락 밴드에서 베이스가 리듬을 만드는 방식이 흥미로워."), "New material is allowed");
Check(memory.NextDirection().Split('\n')[0] == first, "Next paragraph continues same topic");
memory.Remember("두 번째 단락");
Check(memory.NextDirection().Split('\n')[0] == first, "Third paragraph continues same topic");
memory.Remember("세 번째 단락");
Check(memory.NextDirection().Split('\n')[0] != first, "Completed thread changes topic");
for (int i = 0; i < 30; i++) memory.Remember($"발언 {i}");
Check(memory.Recent.Length == 24 && memory.Recent[^1] == "발언 29", "Recent spoken history stays bounded");
