namespace AtomixAI.Core
{
    public class AtomicTaskRequest
    {
        public string ToolId { get; set; }
        public Func<AtomicResult> Action { get; set; }
        public TaskCompletionSource<AtomicResult> Tcs { get; } = new TaskCompletionSource<AtomicResult>();
    }
}
