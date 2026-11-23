using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class FrSys
    {
        private CheckBox chkFeedDistanceCheckEnabled;
        private NumericUpDown nudFeedDistanceStandard;
        private NumericUpDown nudFeedDistanceTolerance;
        private NumericUpDown nudFeedDistanceCompensation;

        private void InitFeedDistanceInspectionControls()
        {
            if (chkFeedDistanceCheckEnabled != null) return;
            TabPage page = new TabPage("\u4e0a\u4e0b\u6599\u9762");
            page.Name = "FeedDistanceInspectionParam";
            page.UseVisualStyleBackColor = true;
            page.AutoScroll = true;
            page.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);

            chkFeedDistanceCheckEnabled = new CheckBox();
            chkFeedDistanceCheckEnabled.Name = "chkFeedDistanceCheckEnabled";
            chkFeedDistanceCheckEnabled.Text = "\u542f\u7528\u4e0a\u4e0b\u6599\u9762\u8ddd\u79bb\u70b9\u68c0";
            chkFeedDistanceCheckEnabled.AutoSize = true;
            chkFeedDistanceCheckEnabled.Location = new Point(24, 24);
            page.Controls.Add(chkFeedDistanceCheckEnabled);

            nudFeedDistanceStandard = CreateFeedDistanceEditor(page, "nudFeedDistanceStandard", "\u6807\u51c6\u8ddd\u79bb (mm)", 72);
            nudFeedDistanceTolerance = CreateFeedDistanceEditor(page, "nudFeedDistanceTolerance", "\u5141\u8bb8\u504f\u5dee (mm)", 118);
            nudFeedDistanceCompensation = CreateFeedDistanceEditor(page, "nudFeedDistanceCompensation", "\u8ddd\u79bb\u8865\u507f (mm)", 164);
            AutoCheckParam.TabPages.Add(page);
            chkFeedDistanceCheckEnabled.CheckedChanged += UpdateFeedDistanceInspectionControls;
            AFC_distance_check_open.CheckedChanged += UpdateFeedDistanceInspectionControls;
            DCC_distance_check_open.CheckedChanged += UpdateFeedDistanceInspectionControls;
            RefreshFeedDistanceInspectionSettings();
        }

        private NumericUpDown CreateFeedDistanceEditor(TabPage page, string name, string caption, int y)
        {
            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.Location = new Point(24, y + 4);
            page.Controls.Add(label);

            NumericUpDown editor = new NumericUpDown();
            editor.Name = name;
            editor.Location = new Point(200, y);
            editor.Size = new Size(160, 28);
            editor.DecimalPlaces = 3;
            editor.Increment = 0.001M;
            editor.Minimum = decimal.MinValue;
            editor.Maximum = decimal.MaxValue;
            editor.TextAlign = HorizontalAlignment.Center;
            editor.ValueChanged += FeedDistanceEditorChanged;
            editor.TextChanged += FeedDistanceEditorChanged;
            page.Controls.Add(editor);
            return editor;
        }

        private void FeedDistanceEditorChanged(object sender, EventArgs e)
        {
            NumericUpDown editor = (NumericUpDown)sender;
            editor.Tag = null;
            editor.BackColor = SystemColors.Window;
        }

        private void LoadFeedDistanceEditor(NumericUpDown editor, double value)
        {
            decimal displayValue = 0;
            bool valid = !double.IsNaN(value) && !double.IsInfinity(value);
            if (valid)
            {
                try { displayValue = (decimal)value; }
                catch (OverflowException) { valid = false; }
            }
            editor.Value = displayValue;
            // Retain invalid persisted values until edited; never silently enable a normalized zero.
            editor.Tag = valid ? null : (object)value;
            editor.BackColor = valid ? SystemColors.Window : Color.MistyRose;
        }

        private void RefreshFeedDistanceInspectionSettings()
        {
            if (chkFeedDistanceCheckEnabled == null) return;
            chkFeedDistanceCheckEnabled.Checked = PT_SET.FeedDistanceCheckEnabled;
            LoadFeedDistanceEditor(nudFeedDistanceStandard, PT_SET.FeedDistanceStandard);
            LoadFeedDistanceEditor(nudFeedDistanceTolerance, PT_SET.FeedDistanceTolerance);
            LoadFeedDistanceEditor(nudFeedDistanceCompensation, PT_SET.FeedDistanceCompensation);
            UpdateFeedDistanceInspectionControls(null, EventArgs.Empty);
        }

        private void UpdateFeedDistanceInspectionControls(object sender, EventArgs e)
        {
            bool enabled = chkFeedDistanceCheckEnabled.Checked;
            nudFeedDistanceStandard.Enabled = enabled;
            nudFeedDistanceTolerance.Enabled = enabled;
            nudFeedDistanceCompensation.Enabled = enabled;
            COM_1.Enabled = enabled || AFC_distance_check_open.Checked || DCC_distance_check_open.Checked;
        }

        internal static string GetFeedDistanceSettingsError(bool enabled, double standard, double tolerance,
            double compensation, string port, int station)
        {
            if (!enabled) return null;
            if (double.IsNaN(standard) || double.IsInfinity(standard) || standard <= 0)
                return "\u6807\u51c6\u8ddd\u79bb\u5fc5\u987b\u662f\u5927\u4e8e0\u7684\u6709\u9650\u6570\u503c (mm)";
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                return "\u5141\u8bb8\u504f\u5dee\u5fc5\u987b\u662f\u975e\u8d1f\u6709\u9650\u6570\u503c (mm)";
            if (double.IsNaN(compensation) || double.IsInfinity(compensation))
                return "\u8ddd\u79bb\u8865\u507f\u5fc5\u987b\u662f\u6709\u9650\u6570\u503c (mm)";
            if (string.IsNullOrWhiteSpace(port))
                return "\u8bf7\u914d\u7f6e\u6574\u5408 COM \u53e3";
            if (station < 1 || station > 4)
                return "\u8bf7\u9009\u62e9\u70b9\u68c0\u5de5\u7ad91\u81f34";
            return null;
        }

        private bool ValidateFeedDistanceInspectionSettings()
        {
            string error = null;
            if (VAR.gsys_set.status == EM_SYS_STA.RUN)
                error = "\u8fd0\u884c\u4e2d\u4e0d\u80fd\u4fdd\u5b58\u53c2\u6570";
            else if (chkFeedDistanceCheckEnabled == null)
                error = "\u4e0a\u4e0b\u6599\u9762\u70b9\u68c0\u53c2\u6570\u5c1a\u672a\u521d\u59cb\u5316";
            else
            {
                error = GetFeedDistanceSettingsError(chkFeedDistanceCheckEnabled.Checked,
                    (double)nudFeedDistanceStandard.Value, (double)nudFeedDistanceTolerance.Value,
                    (double)nudFeedDistanceCompensation.Value, COM_1.Text, CheckWsCombox.SelectedIndex + 1);
                if (error == null && chkFeedDistanceCheckEnabled.Checked)
                {
                    if (nudFeedDistanceStandard.Tag != null || nudFeedDistanceTolerance.Tag != null || nudFeedDistanceCompensation.Tag != null)
                        error = "\u8bf7\u4fee\u6b63\u6807\u7ea2\u7684\u65e0\u6548\u8ddd\u79bb\u53c2\u6570";
                    var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string port in new[] { COM_1.Text, COM_2.Text, COM_3.Text })
                    {
                        if (!string.IsNullOrWhiteSpace(port) && !ports.Add(port))
                            error = "\u8f93\u5165\u7684 COM \u53e3\u6709\u91cd\u590d";
                    }
                }
            }
            if (error == null) return true;
            MessageBox.Show(error, "\u53c2\u6570\u4fdd\u5b58", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void SaveFeedDistanceInspectionSettings()
        {
            PT_SET.FeedDistanceCheckEnabled = chkFeedDistanceCheckEnabled.Checked;
            PT_SET.FeedDistanceStandard = nudFeedDistanceStandard.Tag == null ? (double)nudFeedDistanceStandard.Value : (double)nudFeedDistanceStandard.Tag;
            PT_SET.FeedDistanceTolerance = nudFeedDistanceTolerance.Tag == null ? (double)nudFeedDistanceTolerance.Value : (double)nudFeedDistanceTolerance.Tag;
            PT_SET.FeedDistanceCompensation = nudFeedDistanceCompensation.Tag == null ? (double)nudFeedDistanceCompensation.Value : (double)nudFeedDistanceCompensation.Tag;
        }
    }
}
