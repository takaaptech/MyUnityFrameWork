﻿using UnityEngine;
using System.Collections;
using System.Net.Sockets;
using System.Collections.Generic;
using System;
using System.Net;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;

public class ProtocolService : INetworkInterface 
{
    private const int TYPE_string = 1;
    private const int TYPE_int32 = 2;
    private const int TYPE_double = 3;
    private const int TYPE_bool = 4;
    private const int RT_repeated = 1;
    private const int RT_equired = 0;

    const string m_ProtocolFileName = "ProtocolInfo";
    const string m_methodNameInfoFileName = "MethodInfo";


    Dictionary<string, List<Dictionary<string, object>>> m_protocolInfo;

    Dictionary<int, string> m_methodNameInfo;
    Dictionary<string, int> m_methodIndexInfo;

    private Socket m_Socket;
    private byte[] m_readData = new byte[1024];

    private Thread m_connThread;

    public override void GetIPAddress()
    {
        ReadProtocolInfo();
        ReadMethodNameInfo();

        m_IPaddress = "192.168.0.10";
        m_port = 7001;
    }
    public override void SetIPAddress(string IP, int port)
    {
        ReadProtocolInfo();
        ReadMethodNameInfo();

        m_IPaddress = IP;
        m_port = port;
    }
    public override void Close()
    {
        isConnect = false;
        if (m_Socket != null)
        {
            m_Socket.Close(0);
            m_Socket = null;
        }
        if (m_connThread != null)
        {
            m_connThread.Join();
            m_connThread.Abort();
        }
        m_connThread = null;
    }

    public override void Connect()
    {
        Close();

        m_connThread = null;
        m_connThread = new Thread(new ThreadStart(requestConnect));
        m_connThread.Start();
    }

    //请求数据服务连接线程
    void requestConnect()
    {
        try
        {
            m_ConnectStatusCallback(NetworkState.Connecting);

            m_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Debug.Log(m_IPaddress);
            IPAddress ip = IPAddress.Parse(m_IPaddress);
            
            IPEndPoint ipe = new IPEndPoint(ip, m_port);
            //mSocket.
            m_Socket.Connect(ipe);
            isConnect = true;
            StartReceive();

            m_ConnectStatusCallback(NetworkState.Connected);
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            isConnect = false;
            m_ConnectStatusCallback(NetworkState.FaildToConnect);
        }

    }

    void StartReceive()
    {
        m_Socket.BeginReceive(m_readData, 0, m_readData.Length, SocketFlags.None, new AsyncCallback(EndReceive), m_Socket);
    }
    void EndReceive(IAsyncResult iar) //接收数据
    {
        Socket remote = (Socket)iar.AsyncState;
        int recv = remote.EndReceive(iar);
        if (recv > 0)
        {
            ReceiveDataLoad(m_readData);
        }

        StartReceive();
    }

    //接收Socket数据
    private int head = 0;
    private void ReceiveSorket()
    {
        //在这个线程中接受服务器返回的数据
        byte[] buff = new byte[2];
        while (true)
        {
            if (m_Socket == null) break;
            if (!m_Socket.Connected) break;

            if (head == 0)
            {
                if ((m_Socket.Available) < 2) continue;

                m_Socket.Receive(buff);
                head = (int)buff[1] | ((int)buff[0] << 8);
            }

            if (m_Socket.Available < head) continue;

            byte[] ba = new byte[head];
            m_Socket.Receive(ba);
            head = 0;
            ReceiveDataLoad(ba);
        }
    }

    //发包
    public void Send(byte[] sendbytes)
    {
        try
        {
            m_Socket.Send(sendbytes);
        }
        catch (Exception e)
        {
            Debug.Log("Send Message Error" + e.Message);
        }
    }


    public override void SendMessage(string MessageType,Dictionary<string, object> data)
    {
        ByteArray msg = new ByteArray();

        byte[] message = GetSendByte(MessageType, data);

        int len = 3 + message.Length;
        int method = GetMethodIndex(MessageType);

        msg.WriteShort(len);
        msg.WriteByte((byte)(method/100));
        msg.WriteShort(method);

        if (message != null)
            msg.WriteALLBytes(message);
        else
            msg.WriteInt(0);

        Send(msg.Buffer);
    }


    //解包
    private void ReceiveDataLoad(byte[] bytes)
    {
        try
        {
            //用于做数据处理,加解密,或者压缩于解压缩        
            ByteArray bas = new ByteArray(bytes);

            NetWorkMessage msg = Analysis(bas);
            m_messageCallBack(msg);
        }
        catch(Exception e)
        {
            Debug.LogError(e.ToString());
        }
    }

    private void ConnectCallback(IAsyncResult asyncConnect)
    {

    }

    private void SendCallback(IAsyncResult asyncSend)
    {
    }

    #region 读取protocol信息

    void ReadProtocolInfo()
    {
        m_protocolInfo = new Dictionary<string, List<Dictionary<string, object>>>();
        string content = ResourceManager.ReadTextFile(m_ProtocolFileName);
        AnalysisProtocolStatus currentStatus = AnalysisProtocolStatus.None;
        List<Dictionary<string, object>> msgInfo = new List<Dictionary<string, object>>();

        string[] lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string currentLine = lines[i];

            if (currentStatus == AnalysisProtocolStatus.None)
            {
                if (currentLine.Contains("message"))
                {
                    Regex rgx = new Regex(@"^message\s(\w+)");

                    string msgName = rgx.Match(currentLine).Groups[1].Value;

                    //Debug.Log("message :->" + msgName + "<-");

                    msgInfo = new List<Dictionary<string, object>>();
                    m_protocolInfo.Add(msgName, msgInfo);
                    currentStatus = AnalysisProtocolStatus.Message;
                }
            }
            else
            {
                if (currentLine.Contains("}"))
                {
                    currentStatus = AnalysisProtocolStatus.None;
                    msgInfo = null;
                }
                else
                {
                    if (currentLine.Contains("required"))
                    {
                        Dictionary<string, object> currentFeidInfo = new Dictionary<string, object>();

                        currentFeidInfo.Add("spl", RT_equired);

                        AddName(currentLine, currentFeidInfo);
                        AddType(currentLine, currentFeidInfo);

                        msgInfo.Add(currentFeidInfo);
                    }
                    else if (currentLine.Contains("repeated"))
                    {
                        Dictionary<string, object> currentFeidInfo = new Dictionary<string, object>();

                        currentFeidInfo.Add("spl", RT_repeated);

                        AddName(currentLine, currentFeidInfo);
                        AddType(currentLine, currentFeidInfo);
                    }
                }
            }
        }
    }

    void AddType(string currentLine, Dictionary<string, object> currentFeidInfo)
    {
        if (currentLine.Contains("int32"))
        {
            currentFeidInfo.Add("type", TYPE_int32);
        }
        else if (currentLine.Contains("string"))
        {
            currentFeidInfo.Add("type", TYPE_string);
        }
        else if (currentLine.Contains("double"))
        {
            currentFeidInfo.Add("type", TYPE_double);
        }
        else if (currentLine.Contains("bool"))
        {
            currentFeidInfo.Add("type", TYPE_bool);
        }
        else
        {
            currentFeidInfo.Add("type", currentLine.Split(' ')[1]);
        }
    }

    void AddName(string currentLine, Dictionary<string, object> currentFeidInfo)
    {
        Regex rgx = new Regex(@"^\s+\w+\s+\w+\s+(\w+)");

        //Debug.Log(rgx.Match(currentLine).Value);


        //for (int i = 0; i < rgx.Match(currentLine).Groups.Count; i++)
        //{
        //    Debug.Log(rgx.Match(currentLine).Groups[i]);
        //}

        //Debug.Log(rgx.Match(currentLine).Groups[1]);
        

        currentFeidInfo.Add("name", rgx.Match(currentLine).Groups[1].Value);
    }

    enum AnalysisProtocolStatus
    {
        None,
        Message
    }

    #endregion

    #region 读取消息号映射

    void ReadMethodNameInfo()
    {
        m_methodNameInfo = new Dictionary<int, string>();
        m_methodIndexInfo = new Dictionary<string, int>();

        string content = ResourceManager.ReadTextFile(m_methodNameInfoFileName);

        string[] lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(","))
            {
                Regex rgx = new Regex(@"^(\d+),(\w+)");

                var res = rgx.Match(lines[i]);

                string index = res.Groups[1].Value;
                string indexName = res.Groups[2].Value;

                m_methodNameInfo.Add(int.Parse(index), indexName);
                m_methodIndexInfo.Add(indexName, int.Parse(index));
            }
        }
    }

    int GetMethodIndex(string messageType)
    {
        try
        {
            return m_methodIndexInfo[messageType];
        }
        catch
        {
             throw new Exception("GetMethodIndex ERROR! NOT Find " + messageType);
        }
    }

    #endregion

    #region 解包

    NetWorkMessage  Analysis(ByteArray bytes)
    {
        NetWorkMessage msg = new NetWorkMessage();

        bytes.ReadShort(); //消息长度
        bytes.ReadByte();  //模块名

        int methodIndex = bytes.ReadShort(); //方法名

        msg.m_MessageType = m_methodNameInfo[methodIndex];
        int re_len = bytes.Length - 5;
        msg.m_data = AnalysisData(msg.m_MessageType, bytes.ReadBytes(re_len));

        return msg;
    }

    #region 解析数据

    Dictionary<string, object> AnalysisData(string MessageType, byte[] bytes)
    {
        Dictionary<string, object> data = new Dictionary<string, object>();
        ByteArray ba = new ByteArray(bytes);

        List<Dictionary<string, object>> tableInfo = m_protocolInfo["m_"+MessageType+"_c"];

        for (int i = 0; i < tableInfo.Count; i++)
        {
            int vts = (int)tableInfo[i]["type"];
            int spl = (int)tableInfo[i]["spl"];

            if (vts == TYPE_string)
            {
                if (spl == RT_repeated)
                {
                    data[(string)tableInfo[i]["name"]] = ReadStringList(ba);
                }
                else
                {
                    data[(string)tableInfo[i]["name"]] = ReadString(ba);
                }
            }
            else if (vts == TYPE_bool)
            {
                if (spl == RT_repeated)
                {
                    data[(string)tableInfo[i]["name"]] = ReadBoolList(ba);
                }
                else
                {
                    data[(string)tableInfo[i]["name"]] = ReadBool(ba);
                }
            }
            else if (vts == TYPE_double)
            {
                if (spl == RT_repeated)
                {
                    data[(string)tableInfo[i]["name"]] = ReadDoubleList(ba);
                }
                else
                {
                    data[(string)tableInfo[i]["name"]] = ReadDouble(ba);
                }
            }
            else if (vts == TYPE_int32)
            {
                if (spl == RT_repeated)
                {
                    data[(string)tableInfo[i]["name"]] = ReadIntList(ba);
                }
                else
                {
                    data[(string)tableInfo[i]["name"]] = ReadInt(ba);
                }
            }
            else
            {
                if (spl == RT_repeated)
                {
                    data[(string)tableInfo[i]["name"]] = ReadDictionaryList((string)tableInfo[i]["vp"], ba);
                }
                else
                {
                    data[(string)tableInfo[i]["name"]] = ReadDictionary((string)tableInfo[i]["vp"], ba);
                }
            }
        }

        return data;
    }

    private string ReadString(ByteArray ba)
    {
        uint len = (uint)ba.ReadShort();
        return ba.ReadUTFBytes(len);
    }
    private List<string> ReadStringList(ByteArray ba)
    {
        List<string> tbl = new List<string>();

        int len1 = ba.ReadShort();
        ba.ReadInt();

        for (int i = 0; i < len1; i++)
        {
            tbl.Add(ReadString(ba));
        }
        return tbl;
    }
    private bool ReadBool(ByteArray ba)
    {
        return ba.ReadBoolean();
    }
    private List<bool> ReadBoolList(ByteArray ba)
    {
        List<bool> tbl = new List<bool>();

        int len1 = ba.ReadShort();
        ba.ReadInt();

        for (int i = 0; i < len1; i++)
        {
            tbl.Add(ReadBool(ba));
        }
        return tbl;
    }
    private int ReadInt(ByteArray ba)
    {
        return ba.ReadInt();
    }
    private List<int> ReadIntList(ByteArray ba)
    {
        List<int> tbl = new List<int>();

        int len1 = ba.ReadShort();
        ba.ReadInt();

        for (int i = 0; i < len1; i++)
        {
            int tem_o_read_int = ReadInt(ba);
            tbl.Add(tem_o_read_int);
        }

        return tbl;
    }

    private double ReadDouble(ByteArray ba)
    {
        double tem_double = ba.ReadDouble();
        return Math.Floor(tem_double * 1000) / 1000;
    }
    private List<double> ReadDoubleList(ByteArray ba)
    {
        List<double> tbl = new List<double>();

        int len1 = ba.ReadShort();
        ba.ReadInt();

        for (int i = 0; i < len1; i++)
        {
            tbl.Add(ReadDouble(ba));
        }
        return tbl;
    }

    private Dictionary<string, object> ReadDictionary(string dictName, ByteArray ba)
    {
        int st_len = ba.ReadInt();

        Dictionary<string, object> tbl = new Dictionary<string, object>();

        if (st_len == 0)
        {
            return tbl;
        }

        List<Dictionary<string, object>> tableInfo = m_protocolInfo[dictName];

        for (int i = 0; i < tableInfo.Count; i++)
        {
            int vts = (int)tableInfo[i]["type"];
            int spl = (int)tableInfo[i]["spl"];
            if (vts == TYPE_string)
            {
                if (spl == RT_repeated)
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadStringList(ba);
                }
                else
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadString(ba);
                }
            }
            else if (vts == TYPE_bool)
            {
                if (spl == RT_repeated)
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadBoolList(ba);
                }
                else
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadBool(ba);
                }
            }
            else if (vts == TYPE_double)
            {
                if (spl == RT_repeated)
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadDoubleList(ba);
                }
                else
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadDouble(ba);
                }
            }
            else if (vts == TYPE_int32)
            {

                if (spl == RT_repeated)
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadIntList(ba);
                }
                else
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadInt(ba);
                }
            }
            else
            {
                if (spl == RT_repeated)
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadDictionaryList((string)tableInfo[i]["vp"], ba);
                }
                else
                {
                    tbl[(string)tableInfo[i]["name"]] = ReadDictionary((string)tableInfo[i]["vp"], ba);
                }
            }
        }
        return tbl;
    }

    private List<Dictionary<string, object>> ReadDictionaryList(string str, ByteArray ba)
    {
        List<Dictionary<string, object>> stbl = new List<Dictionary<string, object>>();
        int len1 = ba.ReadShort();
        ba.ReadInt();

        for (int i = 0; i < len1; i++)
        {
            stbl.Add(ReadDictionary(str, ba));
        }
        return stbl;
    }

    #endregion

    #endregion

    #region 发包

    byte[] GetSendByte(string messageType, Dictionary<string, object> data)
    {
        ByteArray Bytes = new ByteArray();

        string messageTypeTemp = "m_" + messageType + "_s";

        if (!m_protocolInfo.ContainsKey(messageTypeTemp))
        {
            //foreach (var item in m_protocolInfo)
            //{
            //    Debug.Log("->"+ item.Key+"<-");
            //}
            throw new Exception("ProtocolInfo NOT Exist ->" + messageTypeTemp + "<-");
        }

        List<Dictionary<string, object>> tableInfo = m_protocolInfo[messageTypeTemp];

        for (int i = 0; i < tableInfo.Count; i++)
        {
            Dictionary<string, object> currentField = tableInfo[i];
            int vts = (int)currentField["type"];
            string jt_name = (string)currentField["name"];
            if (vts == TYPE_string)
            {
                if (data.ContainsKey(jt_name))
                {
                    byte[] bs = Encoding.UTF8.GetBytes((string)data[jt_name]);
                    Bytes.WriteShort(bs.Length);
                    Bytes.WriteALLBytes(bs);
                }
                else
                {
                    Bytes.WriteShort(0);
                }
            }
            else if (vts == TYPE_bool)
            {
                if (data.ContainsKey(jt_name))
                {
                    Bytes.WriteBoolean((bool)data[jt_name]);
                }
            }
            else if (vts == TYPE_double)
            {

                if (data.ContainsKey(jt_name))
                {
                    Bytes.WriteDouble(Convert.ToDouble(data[jt_name].ToString()));
                }
            }
            else if (vts == TYPE_int32)
            {
                if (data.ContainsKey(jt_name))
                {
                    Bytes.WriteInt(Convert.ToInt32(Convert.ToDouble(data[jt_name].ToString())));
                }
            }
            else
            {
                Bytes.WriteALLBytes(GetSendByte((string)currentField["vp"], (Dictionary<string, object>)data[jt_name]));
            }
        }

        return Bytes.Buffer;
    }

    #region 序列化数据



    #endregion

    #endregion
}
