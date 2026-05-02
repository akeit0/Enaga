namespace NativeInlineIme.Windows.Demo;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var instructionLabel = new Label();
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(640, 220);
        instructionLabel.AutoSize = true;
        instructionLabel.Location = new Point(32, 24);
        instructionLabel.Text = "Type here with Japanese IME. This demo uses a standalone IMM32 input method helper.";
        Controls.Add(instructionLabel);
        MinimumSize = new Size(656, 259);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Native Inline IME Demo";
    }

    #endregion
}
