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

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int tipo, IntPtr software, uint flags, IntPtr niveis, uint nNiveis,
        uint sdk, out IntPtr device, out int nivel, out IntPtr contexto);

    [StructLayout(LayoutKind.Sequential)]
    private struct Textura2D
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    private static readonly Guid IidDxgiResource = new("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");
    private static bool? _texturaCompartilhada;

    /// <summary>
    /// O vídeo por OpenGL no Windows passa pelo ANGLE, que entrega cada quadro ao Direct3D numa textura
    /// compartilhada. Onde o driver não compartilha (Wine, alguns drivers e áreas de trabalho remotas), o
    /// Avalonia quebra no meio do desenho e a janela congela pouco depois, sem volta. Aqui se cria uma
    /// textura igual à dele e se pede o compartilhamento; não deu, o vídeo já começa por software.
    /// </summary>
    public static unsafe bool TexturaCompartilhada()
    {
        if (_texturaCompartilhada is { } ja) return ja;
        IntPtr dev = 0, ctx = 0, tex = 0, res = 0;
        bool ok = false;
        try
        {
            // hardware, BGRA (o que o ANGLE pede), SDK 7
            if (D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out dev, out _, out ctx) < 0) return (_texturaCompartilhada = false).Value;
            var d = new Textura2D
            {
                Width = 64, Height = 64, MipLevels = 1, ArraySize = 1, Format = 87 /* B8G8R8A8_UNORM */, SampleCount = 1,
                BindFlags = 0x28 /* RENDER_TARGET | SHADER_RESOURCE */, MiscFlags = 0x100 /* SHARED_KEYEDMUTEX */,
            };
            var vt = *(IntPtr**)dev;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Textura2D*, IntPtr, IntPtr*, int>)vt[5])(dev, &d, 0, &tex) < 0) return (_texturaCompartilhada = false).Value;
            var iid = IidDxgiResource;
            vt = *(IntPtr**)tex;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vt[0])(tex, &iid, &res) < 0) return (_texturaCompartilhada = false).Value;
            IntPtr h = 0;
            vt = *(IntPtr**)res;
            ok = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)vt[8])(res, &h) >= 0 && h != 0;
        }
        catch { ok = false; }
        finally
        {
            foreach (var p in new[] { res, tex, ctx, dev })
                if (p != 0) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)p)[2])(p);
        }
        return (_texturaCompartilhada = ok).Value;
    }
}
