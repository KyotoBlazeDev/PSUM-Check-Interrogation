using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Windows.UI;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class SystemFamilySign : UserControl
{
    private static readonly Lazy<(string family, string source)> Identity = new(ReadIdentity);
    private readonly string family;
    // Original 5 × 7 glyphs rendered as LEDs; no railway fonts or artwork are bundled.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A']="01110/10001/10001/11111/10001/10001/10001", ['B']="11110/10001/10001/11110/10001/10001/11110",
        ['C']="01111/10000/10000/10000/10000/10000/01111", ['D']="11110/10001/10001/10001/10001/10001/11110",
        ['E']="11111/10000/10000/11110/10000/10000/11111", ['F']="11111/10000/10000/11110/10000/10000/10000",
        ['G']="01111/10000/10000/10111/10001/10001/01111", ['H']="10001/10001/10001/11111/10001/10001/10001",
        ['I']="11111/00100/00100/00100/00100/00100/11111", ['J']="00111/00010/00010/00010/10010/10010/01100",
        ['K']="10001/10010/10100/11000/10100/10010/10001", ['L']="10000/10000/10000/10000/10000/10000/11111",
        ['M']="10001/11011/10101/10101/10001/10001/10001", ['N']="10001/11001/10101/10011/10001/10001/10001",
        ['O']="01110/10001/10001/10001/10001/10001/01110", ['P']="11110/10001/10001/11110/10000/10000/10000",
        ['Q']="01110/10001/10001/10001/10101/10010/01101", ['R']="11110/10001/10001/11110/10100/10010/10001",
        ['S']="01111/10000/10000/01110/00001/00001/11110", ['T']="11111/00100/00100/00100/00100/00100/00100",
        ['U']="10001/10001/10001/10001/10001/10001/01110", ['V']="10001/10001/10001/10001/10001/01010/00100",
        ['W']="10001/10001/10001/10101/10101/10101/01010", ['X']="10001/10001/01010/00100/01010/10001/10001",
        ['Y']="10001/10001/01010/00100/00100/00100/00100", ['Z']="11111/00001/00010/00100/01000/10000/11111",
        ['0']="01110/10001/10011/10101/11001/10001/01110", ['1']="00100/01100/00100/00100/00100/00100/01110",
        ['2']="01110/10001/00001/00010/00100/01000/11111", ['3']="11110/00001/00001/01110/00001/00001/11110",
        ['4']="00010/00110/01010/10010/11111/00010/00010", ['5']="11111/10000/10000/11110/00001/00001/11110",
        ['6']="01110/10000/10000/11110/10001/10001/01110", ['7']="11111/00001/00010/00100/01000/01000/01000",
        ['8']="01110/10001/10001/01110/10001/10001/01110", ['9']="01110/10001/10001/01111/00001/00001/01110",
        ['-']="00000/00000/00000/11111/00000/00000/00000", ['.']="00000/00000/00000/00000/00000/00110/00110",
        [' ']="00000/00000/00000/00000/00000/00000/00000", ['?']="01110/10001/00001/00010/00100/00000/00100"
    };

    public SystemFamilySign()
    {
        InitializeComponent();
        var identity = Identity.Value;
        family = identity.family;
        FamilyText.Text = family;
        SourceText.Text = identity.source;
        AutomationProperties.SetName(this, "System family: " + family);
        AutomationProperties.SetAccessibilityView(LedPanel, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    private static (string, string) ReadIdentity()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var bios = machine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", writable: false);
            string? value = bios?.GetValue("SystemFamily") as string;
            return string.IsNullOrWhiteSpace(value)
                ? ("Unavailable", "System Family is not reported by the Windows BIOS registry.")
                : (value.Trim(), "Source: Windows BIOS registry · SystemFamily · read-only");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            DiagnosticsLog.Error("SystemFamily.Read", ex);
            return ("Unavailable", "System Family could not be read from the Windows BIOS registry.");
        }
    }

    private void LedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(family) || e.NewSize.Width < 24) return;
        LedPanel.Children.Clear();
        const double pitch = 4;
        int columns = Math.Max(1, (int)(e.NewSize.Width / (6 * pitch)));
        string text = family.ToUpperInvariant();
        int rows = (text.Length + columns - 1) / columns;
        double height = rows * 9 * pitch;
        if (LedPanel.Height != height) LedPanel.Height = height;
        var lit = new SolidColorBrush(Color.FromArgb(255, 255, 190, 76));
        var dark = new SolidColorBrush(Color.FromArgb(255, 32, 38, 30));
        for (int i = 0; i < text.Length; i++)
        {
            string bits = Glyphs.TryGetValue(text[i], out var glyph) ? glyph : Glyphs['?'];
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 5; x++)
                {
                    var dot = new Ellipse { Width = 2.8, Height = 2.8, Fill = bits[y * 6 + x] == '1' ? lit : dark };
                    Canvas.SetLeft(dot, (i % columns * 6 + x) * pitch);
                    Canvas.SetTop(dot, (i / columns * 9 + y) * pitch);
                    LedPanel.Children.Add(dot);
                }
        }
    }
}
