namespace UnnamedManager.UI;

// What tools/check-layout.ps1 reads (GridProbes.Write). No WinUI here, so AppPaths' JSON
// context, which names LayoutFile, builds in the WPF app too.

public sealed record ProbeRect(string Name, string Area, string Edge, double X, double Y, double Width, double Height);
public sealed record AreaRect(string Name, double Inset, double X, double Y, double Width, double Height);
/// Scale is physical pixels per epx (1.25 at 125%). Edges snap to physical pixels, so at 125% they sit on multiples of 0.8 epx.
public sealed record LayoutFile(double Scale, double WindowWidth, double WindowHeight, List<AreaRect> Areas, List<ProbeRect> Probes);
