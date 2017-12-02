﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ToastNotifications;
using Lua;
using static System.Windows.Forms.Control;
using System.Net;
using Newtonsoft.Json;
using System.ComponentModel;

namespace ESOResearchNotifier
{

    public partial class Form1 : Form
    {
        private enum NotificationType { Balloon, Toast };
        private bool Mute = false;
        private string SavedVars = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Elder Scrolls Online\\live\\SavedVariables");
        private Dictionary<string, string> Config = new Dictionary<string, string>();
        private List<string> SelectedCharacters = new List<string>();
        private string ConfigPath = Application.StartupPath + "\\ESOResearchNotifier.xml";
        private GitHubRelease LatestRelease = new GitHubRelease();
        private string LatestReleaseDownload;
        private bool UpdateReady = false;

        public Form1()
        {
            InitializeComponent();
            Config = XML.ReadData(ConfigPath);
            SelectedCharacters = XML.ReadCharacters(ConfigPath);

            bool timeoutfound = false;
            cboTimeout.Items.Add(new ComboboxItem("1 second", 1000));
            cboTimeout.Items.Add(new ComboboxItem("3 seconds", 3000));
            cboTimeout.Items.Add(new ComboboxItem("5 seconds", 5000));
            cboTimeout.Items.Add(new ComboboxItem("8 seconds", 8000));
            cboTimeout.Items.Add(new ComboboxItem("10 seconds", 10000));
            if (Config.ContainsKey("notifytimeout"))
            {
                foreach (ComboboxItem item in cboTimeout.Items)
                {
                    if (item.Text == Config["notifytimeout"])
                    {
                        timeoutfound = true;
                        cboTimeout.SelectedItem = item;
                        break;
                    }
                }
            }
            if (!timeoutfound)
            {
                cboTimeout.SelectedIndex = 2;
            }

            bool notifystylefound = false;
            cboNotifyStyle.Items.Add(new ComboboxItem("Balloon", NotificationType.Balloon));
            cboNotifyStyle.Items.Add(new ComboboxItem("Toast", NotificationType.Toast));
            if (Config.ContainsKey("notifystyle"))
            {
                foreach (ComboboxItem item in cboNotifyStyle.Items)
                {
                    if (item.Text == Config["notifystyle"])
                    {
                        notifystylefound = true;
                        cboNotifyStyle.SelectedItem = item;
                        break;
                    }
                }
            }
            if (!notifystylefound)
            {
                cboNotifyStyle.SelectedIndex = 0;
            }

            if (Config.ContainsKey("mute"))
            {
                menuItemMute.Checked = Convert.ToBoolean(Config["mute"]);
            }

            fileSystemWatcher1.Path = SavedVars;

            ReadDump();

            if (CheckUpdate())
            {
                btnUpdate.Visible = true;
                btnUpdate.Enabled = true;
            }
        }

        private bool CheckUpdate()
        {
            HttpClient client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });

            client.BaseAddress = new Uri("https://api.github.com");
            client.DefaultRequestHeaders.Add("User-Agent", "ESOResearchNotifier");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            var sResult = client.GetStringAsync("/repos/gobbo1008/ESOResearchNotifier/releases/latest").Result;
            GitHubRelease dResult = JsonConvert.DeserializeObject<GitHubRelease>(sResult);
            LatestRelease = dResult;

            string versionString = LatestRelease.tag_name.Remove(0, 1);
            Version RemoteVersion = new Version(versionString);
            Version LocalVersion = GetType().Assembly.GetName().Version;
            
            if (LocalVersion.CompareTo(RemoteVersion) < 0)
            {
                GitHubDownload dDownload = JsonConvert.DeserializeObject<GitHubDownload>(LatestRelease.assets[0].ToString());
                LatestReleaseDownload = dDownload.browser_download_url;
                return true;
            }
            else
            {
                return false;
            }
        }

        private void DoUpdate()
        {
            WebClient client = new WebClient();
            client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
            client.DownloadFileCompleted += new AsyncCompletedEventHandler(client_DownloadFileCompleted);
            client.DownloadFileAsync(new Uri(LatestReleaseDownload), @"" + Application.StartupPath + "\\ESOResearchNotifier.zip");
        }

        private void client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            btnUpdate.Text = "Update ready!";
            btnUpdate.Visible = true;
            btnUpdate.Enabled = true;
            prgUpdate.Visible = false;
            prgUpdate.Enabled = false;
        }

        private void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            double bytesIn = double.Parse(e.BytesReceived.ToString());
            double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
            double percentage = bytesIn / totalBytes * 100;
            prgUpdate.Value = int.Parse(Math.Truncate(percentage).ToString());
        }

        private void ShowNotification(string Content)
        {
            if (!Mute)
            {
                ComboboxItem NotifyStyle = (ComboboxItem)cboNotifyStyle.SelectedItem;
                ComboboxItem Timeout = (ComboboxItem)cboTimeout.SelectedItem;

                if ((NotificationType)NotifyStyle.Value == NotificationType.Balloon)
                {
                    notifyIcon1.BalloonTipText = Content;
                    notifyIcon1.ShowBalloonTip((int)Timeout.Value);
                }
                else if ((NotificationType)NotifyStyle.Value == NotificationType.Toast)
                {
                    Notification toastNotification = new Notification("ESO Research Notifier", Content, (int)Timeout.Value, FormAnimator.AnimationMethod.Slide, FormAnimator.AnimationDirection.Up);
                    toastNotification.Show();
                }
            }
        }

        private void ResearchDone(object sender, EventArgs e)
        {
            ResearchItem Item = (ResearchItem)sender;
            ShowNotification(Item.ItemName + " - " + Item.TraitName + " has finished for " + Item.CharacterName + "!");
            panelResearch.Controls.Remove(Item);
            Item.Dispose();
        }

        private void ReadDump()
        {
            if (!File.Exists(SavedVars + "\\ResearchDump.lua"))
            {
                MessageBox.Show("Please run the ResearchDump addon first and /reloadui to generate utilisable data");
                return;
            }
            string sTable = File.ReadAllText(SavedVars + "\\ResearchDump.lua");
            Dictionary<string, object> dTable = new Dictionary<string, object>();
            LuaSerializer Reader = new LuaSerializer();
            dTable = (Dictionary<string, object>)Reader.Deserialize(sTable).Value;
            
            treeView1.Nodes.Clear();
            foreach (KeyValuePair<string, object> dAccount in (Dictionary<string, object>)dTable["Default"])
            {
                TreeMetaNode newNode = new TreeMetaNode();
                newNode.Text = dAccount.Key;
                newNode.MetaValue = (Dictionary<string, object>)dAccount.Value;
                foreach (KeyValuePair<string, object> accountCharacters in (Dictionary<string, object>)dAccount.Value)
                {
                    TreeMetaNode newChildNode = new TreeMetaNode();
                    newChildNode.Text = accountCharacters.Key;
                    newChildNode.MetaValue = (Dictionary<string, object>)accountCharacters.Value;
                    if (SelectedCharacters.Contains(accountCharacters.Key))
                    {
                        newChildNode.Checked = true;
                    }
                    newNode.Nodes.Add(newChildNode);
                }
                treeView1.Nodes.Add(newNode);
            }
            if (treeView1.Nodes.Count == 0)
            {
                MessageBox.Show("No valid data could be found. Please run the ResearchDump addon and /reloadui.\r\nIf the problem persists, delete " + SavedVars + "\\ResearchDump.lua and run the addon.\r\nIf the problem still persists, please file an issue on GitHub, attaching your ResearchDump.lua.");
            }
            ExpandTree();
        }

        private void fileSystemWatcher1_Changed(object sender, System.IO.FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(500);
            ReadDump();
            EvaluateTreeView();
        }

        private void menuItemMain_Click(object sender, EventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void menuItemMute_Click(object sender, EventArgs e)
        {
            Mute = menuItemMute.Checked;
            Config["mute"] = Mute.ToString();
            XML.WriteData(ConfigPath, Config, SelectedCharacters);
        }
        
        private void menuItemExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                this.ShowInTaskbar = false;
            }
            else
            {
                this.ShowInTaskbar = true;
            }

            ExpandTree();
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            ShowNotification("This is a test.");
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/gobbo1008/ResearchDump/releases/latest");
        }
        
        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://tamrieltradecentre.com/");
        }

        private void cboNotifyStyle_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboboxItem notifyStyle = (ComboboxItem)cboNotifyStyle.SelectedItem;
            Config["notifystyle"] = notifyStyle.Text;
            XML.WriteData(ConfigPath, Config, SelectedCharacters);
        }

        private void cboTimeout_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboboxItem notifyTimeout = (ComboboxItem)cboTimeout.SelectedItem;
            Config["notifytimeout"] = notifyTimeout.Text;
            XML.WriteData(ConfigPath, Config, SelectedCharacters);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Short, 10-second debug research item
            ResearchItem tItem = new ResearchItem();
            tItem.CraftingDiscipline = ResearchItem.CraftingType.Stable;
            DateTime FinishTime = DateTime.Now.AddSeconds(10);
            tItem.FinishTime = FinishTime;
            tItem.Duration = new TimeSpan(0, 0, 10);
            if (tItem.TimeLeft.Ticks > 0)
            {
                tItem.ResearchDone += new EventHandler(ResearchDone);
                tItem.Setup();
                panelResearch.Controls.Add(tItem);
            }
        }

        private void treeView1_AfterCheck(object sender, TreeViewEventArgs e)
        {
            EvaluateTreeView();
        }

        private void EvaluateTreeView()
        {

            List<string> newSelectedCharacters = new List<string>();
            foreach (Control tempControl in panelResearch.Controls)
            {
                tempControl.Dispose();
            }
            panelResearch.Controls.Clear();

            foreach (TreeMetaNode AccountNode in treeView1.Nodes)
            {
                foreach (TreeMetaNode CharacterNode in AccountNode.Nodes)
                {
                    if (CharacterNode.Checked)
                    {
                        newSelectedCharacters.Add(CharacterNode.Text);
                        foreach (KeyValuePair<string, object> CraftingDiscipline in (Dictionary<string, object>)CharacterNode.MetaValue)
                        {
                            if (CraftingDiscipline.Key == "0")
                            {
                                ResearchItem tItem = new ResearchItem();
                                tItem.CharacterName = CharacterNode.Text;
                                tItem.CraftingDiscipline = ResearchItem.CraftingType.Stable;
                                DateTime FinishTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                                FinishTime = FinishTime.AddSeconds(Convert.ToDouble(CraftingDiscipline.Value)).ToLocalTime();
                                tItem.FinishTime = FinishTime;
                                tItem.Duration = new TimeSpan(20, 0, 0);
                                if (tItem.TimeLeft.Ticks > 0)
                                {
                                    tItem.ResearchDone += new EventHandler(ResearchDone);
                                    tItem.Setup();
                                    panelResearch.Controls.Add(tItem);
                                }
                            }
                            else if (CraftingDiscipline.Key != "version")
                            {
                                ResearchItem.CraftingType craftType = (ResearchItem.CraftingType)Convert.ToInt32(CraftingDiscipline.Key);
                                foreach (KeyValuePair<string, object> item in (Dictionary<string, object>)CraftingDiscipline.Value)
                                {
                                    int itemIndex = Convert.ToInt32(item.Key) - 1;
                                    foreach (KeyValuePair<string, object> trait in (Dictionary<string, object>)item.Value)
                                    {
                                        int traitIndex = Convert.ToInt32(trait.Key) - 1;
                                        double finishTime = 0;
                                        double duration = 0;
                                        foreach (KeyValuePair<string, object> variable in (Dictionary<string, object>)trait.Value)
                                        {
                                            if (variable.Key == "finishTime")
                                            {
                                                finishTime = Convert.ToDouble(variable.Value);
                                            }
                                            else if (variable.Key == "duration")
                                            {
                                                duration = Convert.ToDouble(variable.Value);
                                            }
                                        }
                                        if (finishTime > 0)
                                        {
                                            ResearchItem tItem = new ResearchItem();
                                            tItem.CharacterName = CharacterNode.Text;
                                            tItem.CraftingDiscipline = craftType;
                                            DateTime FinishTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                                            FinishTime = FinishTime.AddSeconds(finishTime).ToLocalTime();
                                            tItem.FinishTime = FinishTime;
                                            DateTime durationHelper = new DateTime();
                                            durationHelper = durationHelper.AddSeconds(duration);
                                            tItem.Duration = durationHelper - DateTime.MinValue;
                                            tItem.TraitIndex = traitIndex;
                                            tItem.ItemIndex = itemIndex;
                                            if (tItem.TimeLeft.Ticks > 0)
                                            {
                                                // MessageBox.Show(tItem.CraftingDiscipline.ToString() + " - " + tItem.ItemName + " - " + tItem.TraitName + " " + tItem.TimeLeftString);
                                                tItem.ResearchDone += new EventHandler(ResearchDone);
                                                tItem.Setup();
                                                panelResearch.Controls.Add(tItem);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            SelectedCharacters = newSelectedCharacters;
            XML.WriteData(ConfigPath, Config, SelectedCharacters);
            ExpandTree();
        }

        private void ExpandTree()
        {
            foreach (TreeMetaNode Node in treeView1.Nodes)
            {
                HideCheckBox(treeView1, Node);
            }
            treeView1.ExpandAll();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            EvaluateTreeView();
        }

        private void btnSortTime_Click(object sender, EventArgs e)
        {
            Utility.SortByTime(panelResearch.Controls);
            Refresh();
        }

        private void btnSortName_Click(object sender, EventArgs e)
        {
            Utility.SortByName(panelResearch.Controls);
        }

        private void btnSortDefault_Click(object sender, EventArgs e)
        {
            EvaluateTreeView();
        }
        
        private void btnUpdate_Click(object sender, EventArgs e)
        {
            if (!UpdateReady)
            {
                btnUpdate.Visible = false;
                btnUpdate.Enabled = false;
                prgUpdate.Visible = true;
                prgUpdate.Enabled = true;
                DoUpdate();
            }
            else
            {
                Close();
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (UpdateReady)
            {
                // Start Updater here
            }
        }

        #region Here be TreeView interop dragons
        // Hack to allow hiding CheckBoxes in the Character selection TreeView

        private const int TVIF_STATE = 0x8;
        private const int TVIS_STATEIMAGEMASK = 0xF000;
        private const int TV_FIRST = 0x1100;
        private const int TVM_SETITEM = TV_FIRST + 63;

        [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Auto)]
        private struct TVITEM
        {
            public int mask;
            public IntPtr hItem;
            public int state;
            public int stateMask;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszText;
            public int cchTextMax;
            public int iImage;
            public int iSelectedImage;
            public int cChildren;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam,
                                                 ref TVITEM lParam);

        /// <summary>
        /// Hides the checkbox for the specified node on a TreeView control.
        /// </summary>
        private void HideCheckBox(FixedTreeView tvw, TreeMetaNode node)
        {
            TVITEM tvi = new TVITEM();
            tvi.hItem = node.Handle;
            tvi.mask = TVIF_STATE;
            tvi.stateMask = TVIS_STATEIMAGEMASK;
            tvi.state = 0;
            SendMessage(tvw.Handle, TVM_SETITEM, IntPtr.Zero, ref tvi);
        }
        #endregion
    }

    public class ComboboxItem
    {
        public string Text { get; set; }
        public object Value { get; set; }

        public ComboboxItem() : this(null, null)
        {
        }

        public ComboboxItem(string Text, object Value)
        {
            this.Text = Text;
            this.Value = Value;
        }

        public override string ToString()
        {
            return Text;
        }
    }

    public class GitHubRelease
    {
        public string tag_name { get; set; }
        public string upload_url { get; set; }
        public object[] assets { get; set; }
    }

    public class GitHubDownload
    {
        public string browser_download_url { get; set; }
    }

    public class TreeMetaNode: TreeNode
    {
        public object MetaValue { get; set; }
    }

    public partial class FixedTreeView : TreeView
    {
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDBLCLK)
            {
                // disable double-click on checkbox to fix Microsoft Vista bug
                TreeViewHitTestInfo tvhti = HitTest(new System.Drawing.Point((int)m.LParam));
                if (tvhti != null && tvhti.Location == TreeViewHitTestLocations.StateImage)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            else if (m.Msg == WM_RBUTTONDOWN)
            {
                // set focus to node on right-click - another Microsoft bug?
                TreeViewHitTestInfo tvhti = HitTest(new System.Drawing.Point((int)m.LParam));
                if (tvhti != null)
                    this.SelectedNode = tvhti.Node;
            }
            base.WndProc(ref m);
        }
    }

    public partial class Utility
    {
        public static void SortByTime(ControlCollection data, int l = 0, int r = -1)
        {
            if (r <= 0)
            {
                r = data.Count - 1;
            }

            int i, j;
            Control x;

            i = l;
            j = r;

            x = data[(l + r) / 2]; // find pivot item

            if (x is ResearchItem)
            {

                while (true)
                {
                    while (((ResearchItem)data[i]).FinishTime < ((ResearchItem)x).FinishTime)
                        i++;
                    while (((ResearchItem)x).FinishTime < ((ResearchItem)data[j]).FinishTime)
                        j--;
                    if (i <= j)
                    {
                        data = exchange(data, i, j);
                        i++;
                        j--;
                    }
                    if (i > j)
                        break;
                }
                if (l < j)
                {
                    SortByTime(data, l, j);
                }
                if (i < r)
                {
                    SortByTime(data, i, r);
                }
            }
        }

        public static void SortByName(ControlCollection data, int l = 0, int r = -1)
        {
            if (r <= 0)
            {
                r = data.Count - 1;
            }

            int i, j;
            Control x;

            i = l;
            j = r;

            x = data[(l + r) / 2]; // find pivot item

            //MessageBox.Show(((ResearchItem)x).LabelText.CompareTo(((ResearchItem)data[j]).LabelText).ToString());

            if (x is ResearchItem)
            {

                while (true)
                {
                    while (((ResearchItem)data[i]).LabelText.CompareTo(((ResearchItem)x).LabelText) < 0)
                        i++;
                    while (((ResearchItem)x).LabelText.CompareTo(((ResearchItem)data[j]).LabelText) < 0)
                        j--;
                    if (i <= j)
                    {
                        data = exchange(data, i, j);
                        i++;
                        j--;
                    }
                    if (i > j)
                        break;
                }
                if (l < j)
                {
                    SortByName(data, l, j);
                }
                if (i < r)
                {
                    SortByName(data, i, r);
                }
            }
        }

        private static ControlCollection exchange(ControlCollection data, int m, int n)
        {
            Control temporary;

            temporary = data[m];
            data.SetChildIndex(data[m], n);
            data.RemoveAt(n);
            data.Add(temporary);
            data.SetChildIndex(data[data.Count - 1], n);

            return data;
        }
    }
}