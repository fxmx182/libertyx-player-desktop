using System.Runtime.InteropServices;

namespace LibertyX.Player.Core;

/// <summary>O pouco de Windows que o .NET não traz pronto. Só é chamado atrás de OperatingSystem.IsWindows().</summary>
public static class Win
{
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string? PastaConhecida(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;

    /// <summary>Vale enquanto a linha que chamou viver — por isso é sempre a da interface.</summary>
    public static void TelaAcesa(bool sim)
    {
        try { SetThreadExecutionState(sim ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous); } catch { }
    }
}
