namespace NativeInlineIme.Windows.Demo;

public partial class Form1 : Form
{
    private readonly ImeTextBoxControl imeTextBox;

    public Form1()
    {
        InitializeComponent();
        imeTextBox = new ImeTextBoxControl
        {
            Location = new Point(32, 72)
        };

        Controls.Add(imeTextBox);
    }
}
