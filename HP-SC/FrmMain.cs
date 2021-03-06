﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using HPSocketCS;
using System.Collections;

namespace HP_SCServer
{
    public enum AppState
    {
        Starting, Started, Stoping, Stoped, Error
    }

    public partial class FrmMain : Form
    {
        private AppState appState = AppState.Stoped;
        private delegate void ShowMsg(string msg);
        private delegate void UpdateGridView(DataTable dt);
        private ShowMsg AddMsgDelegate;
        private UpdateGridView updateGridViewDelegate;
        HPSocketCS.TcpServer server = new HPSocketCS.TcpServer();
        HPSocketCS.Extra<ClientInfo> extra = new HPSocketCS.Extra<ClientInfo>();
        Hashtable htCI = new Hashtable();

        private string title = "TcpServer [ 'C' - 清空数据 ]";

        public FrmMain()
        {
            InitializeComponent();
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            try
            {
                this.Text = title;
                // 本机测试没必要改地址,有需求请注释或删除
                this.txtIpAddress.ReadOnly = true;

                // 加个委托显示msg,因为on系列都是在工作线程中调用的,ui不允许直接操作
                AddMsgDelegate = new ShowMsg(AddMsg);
                updateGridViewDelegate = new UpdateGridView(fnUpdateGridView);

                // 设置服务器事件
                server.OnPrepareListen += new TcpServerEvent.OnPrepareListenEventHandler(OnPrepareListen);
                server.OnAccept += new TcpServerEvent.OnAcceptEventHandler(OnAccept);
                server.OnSend += new TcpServerEvent.OnSendEventHandler(OnSend);
                // 两个数据到达事件的一种
                server.OnPointerDataReceive += new TcpServerEvent.OnPointerDataReceiveEventHandler(OnPointerDataReceive);
                // 两个数据到达事件的一种
                //server.OnReceive += new TcpServerEvent.OnReceiveEventHandler(OnReceive);

                server.OnClose += new TcpServerEvent.OnCloseEventHandler(OnClose);
                server.OnShutdown += new TcpServerEvent.OnShutdownEventHandler(OnShutdown);

                SetAppState(AppState.Stoped);

                AddMsg(string.Format("HP-Socket 版本: {0}", server.Version));
            }
            catch (Exception ex)
            {
                SetAppState(AppState.Error);
                AddMsg(ex.Message);
            }

        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                String ip = this.txtIpAddress.Text.Trim();
                ushort port = ushort.Parse(this.txtPort.Text.Trim());

                // 写在这个位置是上面可能会异常
                SetAppState(AppState.Starting);
                server.IpAddress = ip;
                server.Port = port;
                // 启动服务
                if (server.Start())
                {
                    this.Text = string.Format("{2} - ({0}:{1})", ip, port, title);
                    SetAppState(AppState.Started);
                    AddMsg(string.Format("$服务开启正常 -> ({0}:{1})", ip, port));
                }
                else
                {
                    SetAppState(AppState.Stoped);
                    throw new Exception(string.Format("$服务开启异常 -> {0}({1})", server.ErrorMessage, server.ErrorCode));
                }
            }
            catch (Exception ex)
            {
                AddMsg(ex.Message);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            SetAppState(AppState.Stoping);

            // 停止服务
            AddMsg("$停止服务");
            if (server.Stop())
            {
                this.Text = title;
                SetAppState(AppState.Stoped);
            }
            else
            {
                AddMsg(string.Format("$停止服务异常 -> {0}({1})", server.ErrorMessage, server.ErrorCode));
            }
        }

        private void btnDisconn_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.dgvConn.CurrentRow == null)
                {
                    MessageBox.Show("未选择连接的客户端！");
                    return;
                }

                IntPtr connId = (IntPtr)(Convert.ToInt32(this.dgvConn.CurrentRow.Cells[0].Value.ToString()));
                //IntPtr connId = (IntPtr)Convert.ToInt32(this.txtDisConn.Text.Trim());

                // 断开指定客户
                if (server.Disconnect(connId, true))
                {
                    AddMsg(string.Format("$({0}) 断开客户端正常", connId));
                }
                else
                {
                    throw new Exception(string.Format("断开客户端ID({0}) 出错", connId));
                }
            }
            catch (Exception ex)
            {
                AddMsg(ex.Message);
            }
        }

        HandleResult OnPrepareListen(IntPtr soListen)
        {
            // 监听事件到达了,一般没什么用吧?
            return HandleResult.Ok;
        }

        HandleResult OnAccept(IntPtr connId, IntPtr pClient)
        {
            // 客户进入了
            // 获取客户端ip和端口
            string ip = string.Empty;
            ushort port = 0;
            if (server.GetRemoteAddress(connId, ref ip, ref port))
            {
                AddMsg(string.Format(" > [连接客户端ID:{0}] -> IP地址({1}:{2})", connId, ip.ToString(), port));
            }
            else
            {
                AddMsg(string.Format(" > [连接客户端ID:{0}] -> Server_GetClientAddress() Error", connId));
            }


            //设置附加数据
            ClientInfo clientInfo = new ClientInfo();
            clientInfo.ConnId = connId;
            clientInfo.IpAddress = ip;
            clientInfo.Port = port;

            htCI.Add(connId, clientInfo);
            if (extra.Set(connId, clientInfo) == false)
            {
                AddMsg(string.Format(" > [连接客户端ID:{0}] -> 设置连接附加信息异常", connId));
            }
            else
            {
                //刷新datagridview数据                
                DataTable dt = new DataTable();
                dt.Columns.Add("connid");
                dt.Columns.Add("IP");
                dt.Columns.Add("prot");

                ClientInfo ci = new ClientInfo();
                foreach (var item in htCI.Values)
                {
                    ci = (ClientInfo)item;
                    DataRow dr = dt.NewRow();
                    dr["connid"] = ci.ConnId.ToString();
                    dr["IP"] = ci.IpAddress.ToString();
                    dr["prot"] = ci.Port.ToString();
                    dt.Rows.Add(dr);
                }

                fnUpdateGridView(dt);
            }

            return HandleResult.Ok;
        }

        HandleResult OnSend(IntPtr connId, byte[] bytes)
        {
            // 服务器发数据了
            //AddMsg(string.Format(" > [{0},OnSend] -> ({1} bytes)", connId, bytes.Length));
            return HandleResult.Ok;
        }

        HandleResult OnPointerDataReceive(IntPtr connId, IntPtr pData, int length)
        {
            // 数据到达了
            try
            {
                // 可以通过下面的方法转换到byte[]
                byte[] bytes = new byte[length];
                Marshal.Copy(pData, bytes, 0, length);
                AddMsg(string.Format(" > [获取的数据] -> {0}", Encoding.Default.GetString(bytes)));

                #region -----获取附加数据-----
                /*
                ClientInfo clientInfo = extra.Get(connId);
                if (clientInfo != null)
                {
                    // clientInfo 就是accept里传入的附加数据了
                    AddMsg(string.Format(" > [{0},OnPointerDataReceive] -> {1}:{2} ({3} bytes)", clientInfo.ConnId, clientInfo.IpAddress, clientInfo.Port, length));
                }
                else
                {
                    AddMsg(string.Format(" > [{0},OnPointerDataReceive] -> ({1} bytes)", connId, length));
                }
                */
                #endregion

                //服务器向客户端发送返回数据
                if (server.Send(connId, pData, length))
                {
                    return HandleResult.Ok;
                }

                return HandleResult.Error;
            }
            catch (Exception)
            {

                return HandleResult.Ignore;
            }
        }

        HandleResult OnReceive(IntPtr connId, byte[] bytes)
        {
            // 数据到达了
            try
            {
                // 获取附加数据
                ClientInfo clientInfo = extra.Get(connId);
                if (clientInfo != null)
                {
                    // clientInfo 就是accept里传入的附加数据了
                    AddMsg(string.Format(" > [{0},OnReceive] -> {1}:{2} ({3} bytes)", clientInfo.ConnId, clientInfo.IpAddress, clientInfo.Port, bytes.Length));
                    AddMsg(string.Format(" > [OnReceiveData] -> {0})", Encoding.Default.GetString(bytes)));
                }
                else
                {
                    AddMsg(string.Format(" > [{0},OnReceive] -> ({1} bytes)", connId, bytes.Length));
                }

                if (server.Send(connId, bytes, bytes.Length))
                {
                    return HandleResult.Ok;
                }

                return HandleResult.Error;
            }
            catch (Exception)
            {

                return HandleResult.Ignore;
            }
        }

        HandleResult OnClose(IntPtr connId, SocketOperation enOperation, int errorCode)
        {
            if (errorCode == 0)
                AddMsg(string.Format(" > [关闭客户端ID：{0}]", connId));
            else
                AddMsg(string.Format(" > [客户端ID：{0},错误] -> OP:{1},CODE:{2}", connId, enOperation, errorCode));

            // 获取附加数据
            ClientInfo clientInfo = extra.Get(connId);
            htCI.Remove(clientInfo.ConnId);
            if (extra.Remove(connId) == false)
            {
                AddMsg(string.Format(" > [关闭客户端ID：{0}] -> SetConnectionExtra({0}, null) fail", connId));
            }
            else
            {
                //刷新datagridview数据                
                DataTable dt = new DataTable();
                dt.Columns.Add("connid");
                dt.Columns.Add("IP");
                dt.Columns.Add("prot");                

                ClientInfo ci = new ClientInfo();
                foreach(var item in htCI.Values)
                {
                    ci = (ClientInfo)item;
                    DataRow dr = dt.NewRow();
                    dr["connid"] = ci.ConnId.ToString();
                    dr["IP"] = ci.IpAddress.ToString();
                    dr["prot"] = ci.Port.ToString();
                    dt.Rows.Add(dr);
                }

                fnUpdateGridView(dt);
            }

            return HandleResult.Ok;
        }

        HandleResult OnShutdown()
        {
            // 服务关闭了
            AddMsg(" > [关闭服务]");
            return HandleResult.Ok;
        }

        /// <summary>
        /// 设置程序状态
        /// </summary>
        /// <param name="state"></param>
        void SetAppState(AppState state)
        {
            appState = state;
            this.btnStart.Enabled = (appState == AppState.Stoped);
            this.btnStop.Enabled = (appState == AppState.Started);
            this.txtIpAddress.Enabled = (appState == AppState.Stoped);
            this.txtPort.Enabled = (appState == AppState.Stoped);
            //this.btnDisconn.Enabled = (appState == AppState.Started && this.dgvConn.Rows.Count > 0);
        }

        /// <summary>
        /// 往listbox加一条项目
        /// </summary>
        /// <param name="msg"></param>
        void AddMsg(string msg)
        {
            if (this.lbxMsg.InvokeRequired)
            {
                // 很帅的调自己
                this.lbxMsg.Invoke(AddMsgDelegate, msg);
            }
            else
            {
                if (this.lbxMsg.Items.Count > 100)
                {
                    this.lbxMsg.Items.RemoveAt(0);
                }
                this.lbxMsg.Items.Add(msg);
                this.lbxMsg.TopIndex = this.lbxMsg.Items.Count - (int)(this.lbxMsg.Height / this.lbxMsg.ItemHeight);
            }
        }

        void fnUpdateGridView(DataTable dt)
        {
            if (this.lbxMsg.InvokeRequired)
            {
                // 很帅的调自己
                this.dgvConn.Invoke(updateGridViewDelegate, dt);
            }
            else
            {
                this.dgvConn.DataSource = dt.DefaultView;
            }
        }


        private void lbxMsg_KeyPress(object sender, KeyPressEventArgs e)
        {
            // 清理listbox
            if (e.KeyChar == 'c' || e.KeyChar == 'C')
            {
                this.lbxMsg.Items.Clear();
            }
        }

        private void FrmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            server.Destroy();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public class ClientInfo
    {
        public IntPtr ConnId { get; set; }
        public string IpAddress { get; set; }
        public ushort Port { get; set; }
    }
}
