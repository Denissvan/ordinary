$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
function Read-Source([string]$path) {
    return [IO.File]::ReadAllText((Join-Path $root $path), $utf8)
}
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Get-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    Assert-True ($start -ge 0) "Missing method: $signature"
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}

$settings = Read-Source 'MotionCtrl/MyType.cs'
$fields = @{
    FeedDistanceCheckEnabled = @('bool', 'false', 'Bool')
    FeedDistanceStandard = @('double', '0', 'Double')
    FeedDistanceTolerance = @('double', '0', 'Double')
    FeedDistanceCompensation = @('double', '0', 'Double')
}
foreach ($name in $fields.Keys) {
    $type, $default, $iniType = $fields[$name]
    Assert-True ($settings.Contains("public static $type $name = $default;")) "Missing safe default: $name"
    Assert-True ($settings.Contains("$name = inf.Read$iniType(`"AutoCheckParam`", `"$name`", $default);")) "Missing independent load key: $name"
    Assert-True ($settings.Contains("inf.Write$iniType(`"AutoCheckParam`", `"$name`", $name, ref ischange, true, filename);")) "Missing independent save key: $name"
}
Write-Host 'PASS (static source contract): product defaults and independent AutoCheckParam read/write keys.'

$ui = Read-Source 'UI/FormView/FrSys.FeedDistanceInspection.cs'
$form = Read-Source 'UI/FormView/FrSys.cs'
$schedule = Read-Source 'UI/FormView/FrSys.EnvironmentSchedule.cs'
$validator = Get-Method $ui 'internal static string GetFeedDistanceSettingsError('
Add-Type -TypeDefinition ("using System; public static class FeedDistanceSettingsProbe { " + $validator.Replace('internal static', 'public static') + ' }')
function Check-Settings([bool]$enabled, [double]$standard, [double]$tolerance, [double]$compensation, [string]$port, [int]$station, [bool]$valid) {
    $errorText = [FeedDistanceSettingsProbe]::GetFeedDistanceSettingsError($enabled, $standard, $tolerance, $compensation, $port, $station)
    Assert-True ([string]::IsNullOrEmpty($errorText) -eq $valid) "Unexpected validation result: enabled=$enabled standard=$standard tolerance=$tolerance compensation=$compensation port=$port station=$station"
}
Check-Settings $false 0 0 0 '' 0 $true
Check-Settings $true 100 0 -2 'COM1' 1 $true
Check-Settings $true 100 0.1 2 'COM1' 4 $true
foreach ($invalid in @(0, -1, [double]::NaN, [double]::PositiveInfinity, [double]::NegativeInfinity)) {
    Check-Settings $true $invalid 0 0 'COM1' 1 $false
}
foreach ($invalid in @(-1, [double]::NaN, [double]::PositiveInfinity, [double]::NegativeInfinity)) {
    Check-Settings $true 100 $invalid 0 'COM1' 1 $false
}
foreach ($invalid in @([double]::NaN, [double]::PositiveInfinity, [double]::NegativeInfinity)) {
    Check-Settings $true 100 0 $invalid 'COM1' 1 $false
}
foreach ($port in @('', '   ', $null)) { Check-Settings $true 100 0 0 $port 1 $false }
foreach ($station in @(-1, 0, 5)) { Check-Settings $true 100 0 0 'COM1' $station $false }
Write-Host 'PASS (executable C#): actual settings validator covers disabled, valid, finite values, COM and stations 1..4.'

$init = Get-Method $ui 'private void InitFeedDistanceInspectionControls()'
Assert-True ($init.Contains('AutoCheckParam.TabPages.Add(')) 'Feed controls must be a tab in the existing environment TabControl.'
Assert-True ($init.Contains('"\u4e0a\u4e0b\u6599\u9762"')) 'Missing feed-face tab caption.'
Assert-True ($schedule.Contains('InitFeedDistanceInspectionControls();')) 'Missing OnLoad initialization.'
$show = Get-Method $form 'public void ShowData()'
Assert-True ($show.Contains('RefreshFeedDistanceInspectionSettings();')) 'Product reload must refresh feed controls.'
$get = Get-Method $form 'public bool GetData()'
Assert-True ($get.Contains('if (!ValidateFeedDistanceInspectionSettings()) return false;')) 'GetData must propagate validation failure.'
Assert-True ($get.IndexOf('if (!ValidateFeedDistanceInspectionSettings()) return false;') -lt $get.IndexOf('PT_SET.bpicktray =')) 'Validation must precede all shared setting writes.'
Assert-True ($get.Contains('SaveFeedDistanceInspectionSettings();')) 'GetData must commit feed fields.'
Assert-True ($get.Contains('return true;')) 'GetData must report successful commit.'
$save = Get-Method $form 'private void btn_ptset_save_Click('
Assert-True ($save.Contains('if (!ValidateFeedDistanceInspectionSettings()) return;')) 'Save must preflight feed settings.'
Assert-True ($save.Contains('if (!GetData()) return;')) 'Void/ignored GetData failure must not fall through to file save.'
Assert-True ($save.IndexOf('if (!ValidateFeedDistanceInspectionSettings()) return;') -lt $save.IndexOf('ReloadUpcamTask(')) 'Preflight must precede vision reload side effects.'
Assert-True ($save.IndexOf('if (!GetData()) return;') -lt $save.IndexOf('PT_SET.SavePtCfg(')) 'Commit failure must precede persistence.'
$validate = Get-Method $ui 'private bool ValidateFeedDistanceInspectionSettings()'
Assert-True ($validate.Contains('VAR.gsys_set.status == EM_SYS_STA.RUN')) 'Save must reject RUN.'
Assert-True ($validate.Contains('COM_1.Text')) 'Validate the pending COM editor, not stale PT_SET.'
Assert-True ($validate.Contains('CheckWsCombox.SelectedIndex + 1')) 'Validate the pending station editor.'
$update = Get-Method $ui 'private void UpdateFeedDistanceInspectionControls('
Assert-True ($update -notmatch 'PT_SET\.\w+\s*=') 'Control change events must not write shared settings.'
Assert-True ($update.Contains('COM_1.Enabled =')) 'Feed-only mode must enable the shared COM editor.'
$scheduleUpdate = Get-Method $schedule 'private void UpdateEnvironmentScheduleControls()'
Assert-True ($scheduleUpdate -notmatch 'PT_SET\.\w+\s*=') 'Schedule radio changes must not write runtime settings.'
Assert-True ($get.Contains('PT_SET.EnvironmentCheckScheduleMode =')) 'Schedule mode must commit on Save.'
Write-Host 'PASS (static source contracts): tab, reload, deferred commit, RUN rejection and abort-before-mutation/persistence.'

# Compile the real UI partial against inert settings/controls; no machine Form or device code is loaded.
$harness = @'
namespace MotionCtrl
{
    public enum EM_SYS_STA { STOP, RUN }
    public static class VAR { public static readonly Status gsys_set = new Status(); }
    public sealed class Status { public EM_SYS_STA status; }
    public static class PT_SET
    {
        public static bool FeedDistanceCheckEnabled;
        public static double FeedDistanceStandard, FeedDistanceTolerance, FeedDistanceCompensation;
    }
}
namespace UI
{
    // Intercept warning dialogs so rejected inputs can be tested without displaying a window.
    internal static class MessageBox
    {
        public static System.Windows.Forms.DialogResult Show(string text, string caption,
            System.Windows.Forms.MessageBoxButtons buttons, System.Windows.Forms.MessageBoxIcon icon)
        { return System.Windows.Forms.DialogResult.OK; }
    }
    public partial class FrSys
    {
        private readonly System.Windows.Forms.TabControl AutoCheckParam = new System.Windows.Forms.TabControl();
        private readonly System.Windows.Forms.CheckBox AFC_distance_check_open = new System.Windows.Forms.CheckBox();
        private readonly System.Windows.Forms.CheckBox DCC_distance_check_open = new System.Windows.Forms.CheckBox();
        private readonly System.Windows.Forms.ComboBox COM_1 = new System.Windows.Forms.ComboBox();
        private readonly System.Windows.Forms.ComboBox COM_2 = new System.Windows.Forms.ComboBox();
        private readonly System.Windows.Forms.ComboBox COM_3 = new System.Windows.Forms.ComboBox();
        private readonly System.Windows.Forms.ComboBox CheckWsCombox = new System.Windows.Forms.ComboBox();
        private static void Expect(bool condition, string message)
        { if (!condition) throw new System.Exception(message); }
        public static void RunUiRegression()
        {
            FrSys form = new FrSys();
            form.CheckWsCombox.Items.AddRange(new object[] { "station1", "station2", "station3", "station4" });
            form.InitFeedDistanceInspectionControls();
            form.InitFeedDistanceInspectionControls();
            Expect(form.AutoCheckParam.TabPages.Count == 1, "Initialization must be idempotent.");
            Expect(form.AutoCheckParam.TabPages[0].Text == "\u4e0a\u4e0b\u6599\u9762", "Wrong tab caption.");
            Expect(!form.chkFeedDistanceCheckEnabled.Checked && !form.nudFeedDistanceStandard.Enabled, "Defaults must be disabled.");
            form.chkFeedDistanceCheckEnabled.Checked = true;
            form.nudFeedDistanceStandard.Value = 100;
            form.nudFeedDistanceTolerance.Value = 0.1M;
            form.nudFeedDistanceCompensation.Value = -2;
            form.COM_1.Text = "COM1";
            form.CheckWsCombox.SelectedIndex = 0;
            Expect(form.COM_1.Enabled, "Feed-only must enable COM1.");
            Expect(!MotionCtrl.PT_SET.FeedDistanceCheckEnabled && MotionCtrl.PT_SET.FeedDistanceStandard == 0,
                "UI events must not mutate shared settings.");
            Expect(form.ValidateFeedDistanceInspectionSettings(), "Valid feed-only settings with empty unused ports rejected.");
            form.SaveFeedDistanceInspectionSettings();
            Expect(MotionCtrl.PT_SET.FeedDistanceCheckEnabled && MotionCtrl.PT_SET.FeedDistanceStandard == 100 &&
                MotionCtrl.PT_SET.FeedDistanceTolerance == 0.1 && MotionCtrl.PT_SET.FeedDistanceCompensation == -2, "Save lost settings.");

            form.nudFeedDistanceStandard.Value = 0;
            Expect(!form.ValidateFeedDistanceInspectionSettings(), "Zero standard accepted.");
            Expect(MotionCtrl.PT_SET.FeedDistanceStandard == 100, "Rejected validation mutated settings.");
            form.RefreshFeedDistanceInspectionSettings();
            Expect(form.nudFeedDistanceStandard.Value == 100, "Reload did not discard pending edits.");
            form.COM_2.Text = "com1";
            Expect(!form.ValidateFeedDistanceInspectionSettings(), "Duplicate COM accepted.");
            form.COM_2.Text = "";
            form.CheckWsCombox.SelectedIndex = -1;
            Expect(!form.ValidateFeedDistanceInspectionSettings(), "Invalid station accepted.");
            form.CheckWsCombox.SelectedIndex = 3;
            Expect(form.ValidateFeedDistanceInspectionSettings(), "Station4 rejected.");
            MotionCtrl.VAR.gsys_set.status = MotionCtrl.EM_SYS_STA.RUN;
            Expect(!form.ValidateFeedDistanceInspectionSettings(), "RUN save accepted.");
            MotionCtrl.VAR.gsys_set.status = MotionCtrl.EM_SYS_STA.STOP;

            MotionCtrl.PT_SET.FeedDistanceCompensation = double.NaN;
            form.RefreshFeedDistanceInspectionSettings();
            Expect(form.nudFeedDistanceCompensation.Tag != null, "Invalid persisted value silently normalized.");
            Expect(!form.ValidateFeedDistanceInspectionSettings(), "NaN loaded compensation accepted.");
            form.nudFeedDistanceCompensation.Value = 1;
            Expect(form.ValidateFeedDistanceInspectionSettings(), "Corrected compensation rejected.");
            MotionCtrl.PT_SET.FeedDistanceStandard = double.MaxValue;
            form.RefreshFeedDistanceInspectionSettings();
            Expect(form.nudFeedDistanceStandard.Tag != null, "Overflowed persisted value silently normalized.");
            form.chkFeedDistanceCheckEnabled.Checked = false;
            Expect(form.ValidateFeedDistanceInspectionSettings(), "Invalid configuration cannot be disabled.");
            form.SaveFeedDistanceInspectionSettings();
            Expect(MotionCtrl.PT_SET.FeedDistanceStandard == double.MaxValue && double.IsNaN(MotionCtrl.PT_SET.FeedDistanceCompensation),
                "Disabled save must preserve invalid persisted values until corrected.");
            form.AFC_distance_check_open.Checked = true;
            Expect(form.COM_1.Enabled, "Feed off disabled active AFC COM editor.");
            form.AutoCheckParam.Dispose();
            form.AFC_distance_check_open.Dispose();
            form.DCC_distance_check_open.Dispose();
            form.COM_1.Dispose(); form.COM_2.Dispose(); form.COM_3.Dispose(); form.CheckWsCombox.Dispose();
        }
    }
}
'@
Add-Type -TypeDefinition ($ui + $harness) -ReferencedAssemblies System.Windows.Forms,System.Drawing
[UI.FrSys]::RunUiRegression()
Write-Host 'PASS (executable C# UI-only harness): actual partial compiles, defers edits, reloads, validates, preserves invalid loaded values and supports feed-only COM.'
Write-Host 'No machine application launched. Persistence wiring assertions are static, not an INI round-trip test.'
