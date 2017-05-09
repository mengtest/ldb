using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ldb
{
    public partial class Console : Form
    {

        delegate void SetTextDelegate(string text);

        SetTextDelegate _output;
        SetTextDelegate _error;
        SetTextDelegate _setIP;
        SetTextDelegate _setPort;

        List<string> _history = new List<string>();
        int _historyPos = 0;
        TcpClient _client;
        NetworkStream _netStream;

        string _ip = "127.0.0.1";
        int _port = 10240;
        ByteArray _sendAr = new ByteArray();
        byte[] _readBuf = new byte[4];
        int _readBufLength = 0;
        enum ReadStatus { ReadHead = 0, ReadBody = 1 }
        ReadStatus _readStatus = ReadStatus.ReadHead;
        int _bodyLength = 0;
        string prompt = "lua>";

        public Console()
        {
            InitializeComponent();
            _output = new SetTextDelegate(OnPrint);
            _error = new SetTextDelegate(OnError);
            _setIP = new SetTextDelegate(OnSetIP);
            _setPort = new SetTextDelegate(OnSetPort);

            _sendAr.SetData(new byte[1024], 1024);
        }

        ~Console()
        {
            if (_client.Connected)
            {
                _netStream.Close();
                _client.Close();
            }
        }

        public void Disconnect()
        {
            if (_client != null && _client.Connected)
            {
                _netStream.Close();
                _client.Close();
            }
        }

        public void TryConnect()
        {
            try
            {
                _ip = this.toolStripTextBox1.Text;
                _port = int.Parse(this.toolStripTextBox2.Text);

                _client = new TcpClient();
                _client.BeginConnect(_ip, _port, new AsyncCallback(OnAsyncConnect), _client);
            }
            catch (Exception err)
            {
                Print(err.Message);
            }
        }

        private void Console_Load(object sender, EventArgs e)
        {
            this.textFieldInput.ForeColor = Color.LightGray;
            TryConnect();
        }

        void OnAsyncConnect(IAsyncResult target)
        {
            try
            {
                _client.EndConnect(target);

                _netStream = _client.GetStream();
                _readStatus = ReadStatus.ReadHead;
                _netStream.BeginRead(_readBuf, 0, 4, new AsyncCallback(OnAsyncRead), _netStream);

                Print(string.Format("connected {0}:{1}", _ip, _port));
                Prompt();
            }
            catch (Exception err)
            {
                Print(err.Message);
            }
        }

        void OnAsyncRead(IAsyncResult target)
        {
            try
            {
                if (!_netStream.CanRead)
                {
                    Print("lose connection");
                    return;
                }
                int count = _netStream.EndRead(target);
                if (count == 0)
                {
                    _netStream.Close();
                    _client.Close();
                    Print("lose connection");
                    return;
                }

                _readBufLength += count;
                if (_readStatus == ReadStatus.ReadHead)
                {
                    if (_readBufLength == 4)
                    {
                        _bodyLength = BitConverter.ToInt32(_readBuf, 0);
                        if (_readBuf.Length < _bodyLength)
                        {
                            Array.Resize<byte>(ref _readBuf, _bodyLength);
                        }
                        _readStatus = ReadStatus.ReadBody;
                        _readBufLength = 0;
                        _netStream.BeginRead(_readBuf, 0, _bodyLength, new AsyncCallback(OnAsyncRead), _netStream);
                    }
                    else
                    {
                        _netStream.BeginRead(_readBuf, _readBufLength, 4 - _readBufLength, new AsyncCallback(OnAsyncRead), _netStream);
                    }
                }
                else if (_readStatus == ReadStatus.ReadBody)
                {
                    if (_readBufLength == _bodyLength)
                    {
                        //TODO:parse
                        string text = System.Text.UTF8Encoding.UTF8.GetString(_readBuf, 0, _readBufLength);
                        string cmd = text;
                        string tail = "";
                        int sep = text.IndexOf(' ');
                        if (sep > 0)
                        {
                            cmd = text.Substring(0, sep);
                            tail = text.Substring(sep + 1);
                        }
                        string methodName = "Cmd_" + cmd;
                        var mi = this.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (mi != null)
                        {
                            mi.Invoke(this, new object[] { tail });
                        }

                        _readStatus = ReadStatus.ReadHead;
                        _readBufLength = 0;
                        _netStream.BeginRead(_readBuf, 0, 4, new AsyncCallback(OnAsyncRead), _netStream);
                    }
                    else
                    {
                        _netStream.BeginRead(_readBuf, _readBufLength, _bodyLength - _readBufLength, new AsyncCallback(OnAsyncRead), _netStream);
                    }
                }
            }
            catch (Exception e)
            {
                Print(e.Message);
            }
        }

        void SaveHistory(string line)
        {
            _history.Add(line);
            _historyPos = _history.Count;
        }

        void SendCommand(string cmd)
        {
            if (_client != null && _client.Connected)
            {
                _sendAr.Clear();
                byte[] bodyBuf = System.Text.Encoding.UTF8.GetBytes(cmd);
                _sendAr.WriteInt(bodyBuf.Length);
                _sendAr.Write(bodyBuf);
                _netStream.Write(_sendAr.getData(), 0, _sendAr.Position);
            }
            else
            {
                Print("lose connection\n");
            }
        }

        public void Error(string text)
        {
            this.textFieldOutput.Invoke(_error, text + "\n");
        }

        public void Print(string text)
        {
            this.textFieldOutput.Invoke(_output, text + "\n");
        }

        void Prompt()
        {
            this.textFieldOutput.Invoke(_output, prompt);
        }

        void OnError(string text)
        {
            text = text.Replace("\r\n", "\n");
            this.textFieldOutput.Focus();
            this.textFieldOutput.AppendText("");
            this.textFieldOutput.SelectionColor = Color.Red;
            this.textFieldOutput.AppendText(text);
            this.textFieldInput.Focus();
        }

        void OnPrint(string text)
        {
            text = text.Replace("\r\n", "\n");
            this.textFieldOutput.Focus();
            this.textFieldOutput.AppendText("");
            this.textFieldOutput.SelectionColor = Color.Green;
            this.textFieldOutput.AppendText(text);
            this.textFieldInput.Focus();
        }

        void textFieldInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && !e.Control)
            {
                ReloadHistory(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && !e.Control)
            {
                ReloadHistory(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Return && !e.Control)
            {
                string line = this.textFieldInput.Text;
                this.textFieldInput.Text = "";
                Print(line);

                if (line.Length > 0)
                {
                    SaveHistory(line);
                }
                SendCommand(line);
                e.Handled = true;
            }
        }

        void ReloadHistory(int offset)
        {
            _historyPos += offset;
            if (_historyPos < 0)
            {
                _historyPos = 0;
                return;
            }
            if (_historyPos >= _history.Count)
            {
                _historyPos = _history.Count;
                SetCommand("");
                return;
            }
            string line = _history[_historyPos];
            SetCommand(line);
        }

        void SetCommand(string cmd)
        {
            textFieldInput.Text = cmd;
            textFieldInput.SelectionStart = cmd.Length;
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            Disconnect();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            Disconnect();
            TryConnect();
        }

        private void toolStripButton3_Click(object sender, EventArgs e)
        {
            this.textFieldOutput.ResetText();
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            IPList list = new IPList(this);
            list.ShowDialog();
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
            Process ps = new Process
            {
                StartInfo =
                {
                    FileName = "adb",
                    Arguments = "shell \"netcfg | grep wlan0\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            ps.OutputDataReceived += (object s, DataReceivedEventArgs a) =>
            {
                if (a.Data != null)
                {
                    Regex r = new Regex(@"(\d+\.\d+\.\d+\.\d+)");
                    Match m = r.Match(a.Data);
                    if (m.Length > 0)
                    {
                        SetIP(m.Captures[0].Value);
                    }
                }
            };
            ps.ErrorDataReceived += (object s, DataReceivedEventArgs a) =>
            {
                if (a.Data != null)
                {
                    Print(a.Data);
                }
            };
            ps.Start();
            ps.BeginOutputReadLine();
            ps.BeginErrorReadLine();
        }
        private void toolStripButton6_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Multiselect = false;
            dlg.InitialDirectory = ".";
            dlg.Filter = "Text Files|*.lua;*.text;*.bytes;*.json";

            DialogResult result = dlg.ShowDialog();
            if (result == DialogResult.OK)
            {
                using (FileStream fs = File.OpenRead(dlg.FileName))
                {
                    byte[] bytes = new byte[fs.Length];
                    fs.Read(bytes, 0, bytes.Length);
                    string text = System.Text.UTF8Encoding.UTF8.GetString(bytes);
                    SendCommand(text);
                }
            }
        }

        public void SetIP(string ip)
        {
            this.Invoke(_setIP, ip);
        }

        void OnSetIP(string ip)
        {
            this.toolStripTextBox1.Text = ip;
            _ip = ip;
        }

        public void SetPort(string port)
        {
            this.Invoke(_setPort, port);
        }

        void OnSetPort(string port)
        {
            this.toolStripTextBox2.Text = port;
            _port = int.Parse(port);
        }

        void Cmd_print(string tail)
        {
            Print(tail);
        }

        void Cmd_ret(string tail)
        {
            int sep = tail.IndexOf(' ');
            string status = "ok";
            string err = null;
            if (sep > 0)
            {
                status = tail.Substring(0, sep);
                err = tail.Substring(sep);
            }
            Prompt();
            if (status != "ok")
            {
                Error(err);
            }
        }

        void Cmd_quit(string tail)
        {
            Disconnect();
        }

        void Cmd_break(string tail)
        {
            prompt = "ldb>";
            string[] args = tail.Split(',');
            Print(string.Format("break at {0}:{1}", args[0], args[1]));
            Prompt();
        }

        void Cmd_resume(string tail)
        {
            prompt = "lua>";
            Prompt();
        }
    }
}