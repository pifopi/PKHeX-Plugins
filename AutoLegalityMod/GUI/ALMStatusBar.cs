using System;
using System.Windows.Forms;

namespace AutoModPlugins.GUI;

public partial class ALMStatusBar : Form
{
    private int _count;
    private int _maxTasks;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            pb_status.Value = Math.Min(_count, pb_status.Maximum);
            L_status.Text = $"{_count}/{_maxTasks} completed";
        }
    }

    public ALMStatusBar(string title, int amountOftasks)
    {
        InitializeComponent();
        this.Text = title;
        _maxTasks = amountOftasks;
        pb_status.Maximum = amountOftasks;
        Count = 0;
    }
}