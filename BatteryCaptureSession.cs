using System;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

// Used on the UI thread: dashboard and inspection requests share one in-flight query.
internal sealed class BatteryCaptureSession(Func<Task<BatteryDiagnostics>> read)
{
    private Task<BatteryDiagnostics>? pending;
    public event Action<BatteryDiagnostics>? Captured;

    public Task<BatteryDiagnostics> ReadAsync()
    {
        if (pending is not null) return pending;
        var completion = new TaskCompletionSource<BatteryDiagnostics>();
        pending = completion.Task;
        _ = CompleteAsync(completion);
        return completion.Task;
    }

    private async Task CompleteAsync(TaskCompletionSource<BatteryDiagnostics> completion)
    {
        try
        {
            var sample = await read();
            Captured?.Invoke(sample);
            pending = null;
            completion.SetResult(sample);
        }
        catch (Exception ex)
        {
            pending = null;
            completion.SetException(ex);
        }
    }
}
