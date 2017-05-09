using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ldb
{
    public partial class IPList : Form
    {
        delegate void ClearListDelegate();
        delegate void AddItemDelegate(ListViewItem it);

        volatile UdpClient _udpClient;
        IPEndPoint _udpClientEP;
        Console _console;
        ClearListDelegate _clear;
        AddItemDelegate _add;
        Thread _boardCastThread;
        bool _activated = false;

        public IPList(Console console)
        {
            InitializeComponent();

            _clear = new ClearListDelegate(OnClearListView);
            _add = new AddItemDelegate(OnAddListViewItem);

            _console = console;
            _udpClientEP = new IPEndPoint(IPAddress.Any, 12042);
            _udpClient = new UdpClient(_udpClientEP);
            _udpClient.BeginReceive(new AsyncCallback(onLDBHostReply), _udpClient);
        }

        protected override void OnActivated(EventArgs e)
        {
            if (!_activated)
            {
                _activated = true;
                ThreadStart s = new ThreadStart(BoardCastThread);
                _boardCastThread = new Thread(s);
                _boardCastThread.Start();
            }
        }

        private void OnAddListViewItem(ListViewItem it)
        {
            this.listView1.Items.Add(it);
        }

        private void OnClearListView()
        {
            this.listView1.Items.Clear();
        }

        void BoardCastThread()
        {
            if (_udpClient == null)
            {
                return;
            }

            this.listView1.Invoke(_clear);

            string innerIP = null;
            string hostName = Dns.GetHostName();
            IPHostEntry localhost = Dns.GetHostEntry(hostName);
            foreach (var ip in localhost.AddressList)
            {
                if (IsInnerIP(ip.ToString()))
                {
                    innerIP = ip.ToString();
                    break;
                }
            }

            IPEndPoint broadCastEP = new IPEndPoint(IPAddress.Broadcast, 10241);
            string text = "are you ok?";
            byte[] bytes = System.Text.UTF8Encoding.UTF8.GetBytes(text);
            _udpClient.Send(bytes, bytes.Length, broadCastEP);

            if (!string.IsNullOrEmpty(innerIP))
            {
                string[] strs = innerIP.Split('.');
                byte[] myAddress = new byte[strs.Length];
                byte[] address = new byte[strs.Length];
                for (int i = 0; i < strs.Length; ++i)
                {
                    myAddress[i] = byte.Parse(strs[i]);
                    address[i] = byte.Parse(strs[i]);
                }

                for (byte i = 1; i < 255; ++i)
                {
                    if (i != myAddress[2])
                    {
                        address[2] = i;
                        for (byte j = 1; j < 255; ++j)
                        {
                            address[3] = j;
                            IPAddress ipa = new IPAddress(address);
                            IPEndPoint ep = new IPEndPoint(ipa, 10241);
                            try
                            {
                                _udpClient.SendAsync(bytes, bytes.Length, ep);
                            }
                            catch
                            {
                                return;
                            }
                        }
                        Thread.Sleep(10);
                    }
                }
            }
        }

        /// <summary>
        /// 判断IP地址是否为内网IP地址
        /// </summary>
        /// <param name="ipAddress">IP地址字符串</param>
        /// <returns></returns>
        private bool IsInnerIP(String ipAddress)
        {
            if (ipAddress.Split('.').Length != 4)
            {
                return false;
            }

            bool isInnerIp = false;
            long ipNum = GetIpNum(ipAddress);
            /**
               私有IP：A类  10.0.0.0-10.255.255.255
                       B类  172.16.0.0-172.31.255.255
                       C类  192.168.0.0-192.168.255.255
                       当然，还有127这个网段是环回地址   
              **/
            long aBegin = GetIpNum("10.0.0.0");
            long aEnd = GetIpNum("10.255.255.255");
            long bBegin = GetIpNum("172.16.0.0");
            long bEnd = GetIpNum("172.31.255.255");
            long cBegin = GetIpNum("192.168.0.0");
            long cEnd = GetIpNum("192.168.255.255");
            isInnerIp = IsInner(ipNum, aBegin, aEnd) || IsInner(ipNum, bBegin, bEnd) || IsInner(ipNum, cBegin, cEnd) || ipAddress.Equals("127.0.0.1");
            return isInnerIp;
        }
        /// <summary>
        /// 把IP地址转换为Long型数字
        /// </summary>
        /// <param name="ipAddress">IP地址字符串</param>
        /// <returns></returns>
        private long GetIpNum(String ipAddress)
        {
            String[] ip = ipAddress.Split('.');
            long a = int.Parse(ip[0]);
            long b = int.Parse(ip[1]);
            long c = int.Parse(ip[2]);
            long d = int.Parse(ip[3]);

            long ipNum = a * 256 * 256 * 256 + b * 256 * 256 + c * 256 + d;
            return ipNum;
        }
        /// <summary>
        /// 判断用户IP地址转换为Long型后是否在内网IP地址所在范围
        /// </summary>
        /// <param name="userIp"></param>
        /// <param name="begin"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        private bool IsInner(long userIp, long begin, long end)
        {
            return (userIp >= begin) && (userIp <= end);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
        }

        ~IPList()
        {
            if (_boardCastThread != null)
            {
                _boardCastThread.Join();
            }
            OnClosed(null);
        }

        private void onLDBHostReply(IAsyncResult ar)
        {
            try
            {
                UdpClient client = ar.AsyncState as UdpClient;
                if (client == null)
                {
                    return;
                }

                IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, 10241);
                Byte[] receiveBytes = client.EndReceive(ar, ref ep);

                List<string> lst = new List<string>();
                int offset = 0;
                while (offset < receiveBytes.Length)
                {
                    int strLen = BitConverter.ToInt32(receiveBytes, offset);
                    offset += 4;

                    if (strLen + offset > receiveBytes.Length)
                    {
                        break;
                    }

                    string s = System.Text.UTF8Encoding.UTF8.GetString(receiveBytes, offset, strLen);
                    offset += strLen;

                    lst.Add(s);
                }

                if (lst.Count == 3)
                {
                    ListViewItem it = new ListViewItem(lst[0]);
                    it.SubItems.Add(lst[1]);
                    it.SubItems.Add(ep.Address.ToString());
                    it.SubItems.Add(lst[2]);
                    this.listView1.Invoke(_add, it);
                }
                client.BeginReceive(new AsyncCallback(onLDBHostReply), client);
            }
            catch (ObjectDisposedException)
            {
                //忽略
            }
            catch (Exception e)
            {
                _console.Error(e.Message);
            }
        }

        private void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.listView1.SelectedItems.Count > 0)
            {
                ListViewItem it = this.listView1.SelectedItems[0];
                _console.SetIP(it.SubItems[2].Text);
                _console.SetPort(it.SubItems[3].Text);
                _console.Disconnect();
                _console.TryConnect();
                this.Close();
            }
        }
    }
}
