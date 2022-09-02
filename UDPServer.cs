using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Force.Crc32;
using System.Numerics;

namespace OctoPoint
{
    class UDPServer
    {
        private Socket udpSock;
        private uint serverId;
        private bool running;
        private byte[] recvBuffer = new byte[1024];

        private const ushort MaxProtocolVersion = 1001;
        enum MessageType
        {
            DSUC_VersionReq = 0x100000,
            DSUS_VersionRsp = 0x100000,
            DSUC_ListPorts = 0x100001,
            DSUS_PortInfo = 0x100001,
            DSUC_PadDataReq = 0x100002,
            DSUS_PadDataRsp = 0x100002,
        };

        IPEndPoint connectedClient = null;
        long lastRequestAt = 0;
        const int clientTimeoutLimit = 5000;
        int packetCounter = 0;

        public Vector3 currentRotation = new Vector3(0, 0, 0);

        public void Start(IPAddress ip, int port = 26760)
        {
            System.Diagnostics.Debug.WriteLine("Start called!");

            //if (!Boolean.Parse(ConfigurationManager.AppSettings["MotionServer"]))
            //{
            //    form.console.AppendText("Motion server is OFF.\r\n");
            //    return;
            //}

            if (running)
            {
                if (udpSock != null)
                {
                    udpSock.Close();
                    udpSock = null;
                }
                running = false;
            }

            udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try { udpSock.Bind(new IPEndPoint(ip, port)); }
            catch (SocketException)
            {
                udpSock.Close();
                udpSock = null;

                System.Diagnostics.Debug.WriteLine("Could not start server. Make sure that only one instance of the program is running at a time and no other CemuHook applications are running.\r\n");
                return;
            }

            byte[] randomBuf = new byte[4];
            new Random().NextBytes(randomBuf);
            serverId = BitConverter.ToUInt32(randomBuf, 0);

            running = true;
            System.Diagnostics.Debug.WriteLine(String.Format("Starting server on {0}:{1}\r\n", ip.ToString(), port));

            StartReceive();
        }

        private void StartReceive()
        {

            try
            {
                if (running)
                {
                    //Start listening for a new message.
                    EndPoint newClientEP = new IPEndPoint(IPAddress.Any, 0);
                    udpSock.BeginReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref newClientEP, ReceiveCallback, udpSock);
                }
            }
            catch (SocketException)
            {
                uint IOC_IN = 0x80000000;
                uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                udpSock.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);

                StartReceive();
            }
        }

        private void ReceiveCallback(IAsyncResult iar)
        {
            byte[] localMsg = null;
            EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                //Get the received message.
                Socket recvSock = (Socket)iar.AsyncState;
                int msgLen = recvSock.EndReceiveFrom(iar, ref clientEP);

                localMsg = new byte[msgLen];
                Array.Copy(recvBuffer, localMsg, msgLen);
            }
            catch (Exception) { }

            //Start another receive as soon as we copied the data
            StartReceive();

            //Process the data if its valid
            if (localMsg != null)
            {
                ProcessIncoming(localMsg, (IPEndPoint)clientEP);
            }
        }

        private void ProcessIncoming(byte[] localMsg, IPEndPoint clientEP)
        {
            try
            {
                int currIdx = 0;
                if (localMsg[0] != 'D' || localMsg[1] != 'S' || localMsg[2] != 'U' || localMsg[3] != 'C')
                    return;
                else
                    currIdx += 4;

                uint protocolVer = BitConverter.ToUInt16(localMsg, currIdx);
                currIdx += 2;

                if (protocolVer > MaxProtocolVersion)
                    return;

                uint packetSize = BitConverter.ToUInt16(localMsg, currIdx);
                currIdx += 2;

                if (packetSize < 0)
                    return;

                packetSize += 16; //size of header
                if (packetSize > localMsg.Length)
                    return;
                else if (packetSize < localMsg.Length)
                {
                    byte[] newMsg = new byte[packetSize];
                    Array.Copy(localMsg, newMsg, packetSize);
                    localMsg = newMsg;
                }

                uint crcValue = BitConverter.ToUInt32(localMsg, currIdx);
                //zero out the crc32 in the packet once we got it since that's whats needed for calculation
                localMsg[currIdx++] = 0;
                localMsg[currIdx++] = 0;
                localMsg[currIdx++] = 0;
                localMsg[currIdx++] = 0;

                uint crcCalc = Crc32Algorithm.Compute(localMsg);
                if (crcValue != crcCalc)
                    return;

                uint clientId = BitConverter.ToUInt32(localMsg, currIdx);
                currIdx += 4;

                uint messageType = BitConverter.ToUInt32(localMsg, currIdx);
                currIdx += 4;

                if (messageType == (uint)MessageType.DSUC_VersionReq)
                {
                    //byte[] outputData = new byte[8];
                    //int outIdx = 0;
                    //Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_VersionRsp), 0, outputData, outIdx, 4);
                    //outIdx += 4;
                    //Array.Copy(BitConverter.GetBytes((ushort)MaxProtocolVersion), 0, outputData, outIdx, 2);
                    //outIdx += 2;
                    //outputData[outIdx++] = 0;
                    //outputData[outIdx++] = 0;

                    //SendPacket(clientEP, outputData, 1001);
                    System.Diagnostics.Debug.WriteLine("Version request ignored.");
                }
                else if (messageType == (uint)MessageType.DSUC_ListPorts)
                {
                    // Requested information on gamepads - return MAC address
                    int numPadRequests = BitConverter.ToInt32(localMsg, currIdx);
                    currIdx += 4;
                    if (numPadRequests < 0 || numPadRequests > 4)
                        return;

                    int requestsIdx = currIdx;
                    for (int i = 0; i < numPadRequests; i++)
                    {
                        byte currRequest = localMsg[requestsIdx + i];
                        if (currRequest < 0 || currRequest > 4)
                            return;
                    }

                    byte[] outputData = new byte[16];
                    for (byte i = 0; i < numPadRequests; i++)
                    {
                        byte currRequest = localMsg[requestsIdx + i];
                        //var padData = controllers[i];//controllers[currRequest];

                        int outIdx = 0;
                        Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_PortInfo), 0, outputData, outIdx, 4);
                        outIdx += 4;

                        outputData[outIdx++] = 0x00;
                        outputData[outIdx++] = 0x02;
                        outputData[outIdx++] = 0x03;
                        outputData[outIdx++] = 0x01;

                        //var addressBytes = padData.PadMacAddress.GetAddressBytes();
                        //if (addressBytes.Length == 6)
                        //{
                        //    outputData[outIdx++] = addressBytes[0];
                        //    outputData[outIdx++] = addressBytes[1];
                        //    outputData[outIdx++] = addressBytes[2];
                        //    outputData[outIdx++] = addressBytes[3];
                        //    outputData[outIdx++] = addressBytes[4];
                        //    outputData[outIdx++] = addressBytes[5];
                        //}
                        //else
                        //{
                        //    outputData[outIdx++] = 0;
                        //    outputData[outIdx++] = 0;
                        //    outputData[outIdx++] = 0;
                        //    outputData[outIdx++] = 0;
                        //    outputData[outIdx++] = 0;
                        //    outputData[outIdx++] = 0;
                        //}

                        // mac address
                        for (int j = 0; j < 5; j++)
                        {
                            outputData[outIdx++] = 0;
                        }

                        outputData[outIdx++] = 0xff; // 00:00:00:00:00:FF
                        outputData[outIdx++] = 0; // dunno (probably 'is active')

                        SendPacket(clientEP, outputData, 1001); // enable this
                    }
                }
                else if (messageType == (uint)MessageType.DSUC_PadDataReq)
                {
                    byte regFlags = localMsg[currIdx++];
                    byte idToReg = localMsg[currIdx++];
                    PhysicalAddress macToReg = null;
                    {
                        byte[] macBytes = new byte[6];
                        Array.Copy(localMsg, currIdx, macBytes, 0, macBytes.Length);
                        currIdx += macBytes.Length;
                        macToReg = new PhysicalAddress(macBytes);
                    }

                    //lock (clients)
                    //{
                    //    if (clients.ContainsKey(clientEP))
                    //        clients[clientEP].RequestPadInfo(regFlags, idToReg, macToReg);
                    //    else
                    //    {
                    //        var clientTimes = new ClientRequestTimes();
                    //        clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);
                    //        clients[clientEP] = clientTimes;
                    //    }
                    //}

                    if (regFlags == 0 || (idToReg == 0 && (regFlags & 0x01) != 0) || (macToReg == PhysicalAddress.Parse("00:00:00:00:00:ff") && (regFlags & 0x02) != 0))
                    {
                        connectedClient = clientEP;
                        lastRequestAt = DateTimeOffset.Now.ToUnixTimeSeconds();
                    }
                }
            }
            catch (Exception) { }
        }

        private void SendPacket(IPEndPoint clientEP, byte[] usefulData, ushort reqProtocolVersion = MaxProtocolVersion)
        {
            byte[] packetData = new byte[usefulData.Length + 16];
            int currIdx = BeginPacket(packetData, reqProtocolVersion);
            Array.Copy(usefulData, 0, packetData, currIdx, usefulData.Length);
            FinishPacket(packetData);

            try { udpSock.SendTo(packetData, clientEP); } catch (Exception) { }
        }

        private int BeginPacket(byte[] packetBuf, ushort reqProtocolVersion = MaxProtocolVersion)
        {
            int currIdx = 0;
            packetBuf[currIdx++] = (byte)'D';
            packetBuf[currIdx++] = (byte)'S';
            packetBuf[currIdx++] = (byte)'U';
            packetBuf[currIdx++] = (byte)'S';

            Array.Copy(BitConverter.GetBytes((ushort)reqProtocolVersion), 0, packetBuf, currIdx, 2);
            currIdx += 2;

            Array.Copy(BitConverter.GetBytes((ushort)packetBuf.Length - 16), 0, packetBuf, currIdx, 2);
            currIdx += 2;

            Array.Clear(packetBuf, currIdx, 4); //place for crc
            currIdx += 4;

            Array.Copy(BitConverter.GetBytes((uint)serverId), 0, packetBuf, currIdx, 4);
            currIdx += 4;

            return currIdx;
        }

        private void FinishPacket(byte[] packetBuf)
        {
            Array.Clear(packetBuf, 8, 4);

            uint crcCalc = Crc32Algorithm.Compute(packetBuf);
            Array.Copy(BitConverter.GetBytes((uint)crcCalc), 0, packetBuf, 8, 4);
        }

        public void sendMotionData(Vector3 gyro, bool lmb, bool rmb)
        {
            ulong motionTimestamp = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000;

            Vector3 accelerometer = new Vector3(0, 0, 0);
            IPEndPoint client = connectedClient;
            if (client == null || DateTimeOffset.Now.ToUnixTimeSeconds() - lastRequestAt > clientTimeoutLimit)
                return;

            byte[] outputData = new byte[100];
            int outIdx = BeginPacket(outputData, 1001);
            Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_PadDataRsp), 0, outputData, outIdx, 4);
            outIdx += 4;

            outputData[outIdx++] = 0x00; // pad id
            outputData[outIdx++] = 0x02; // state (connected)
            outputData[outIdx++] = 0x02; // model (generic)
            outputData[outIdx++] = 0x01; // connection type (usb)

            for (int i = 0; i < 5; i++)
            {
                outputData[outIdx++] = 0x00;
            }
            outputData[outIdx++] = 0xff; // 00:00:00:00:00:FF

            outputData[outIdx++] = 0xef; // battery (charged)
            outputData[outIdx++] = 1; // is active (true)

            Array.Copy(BitConverter.GetBytes(packetCounter++), 0, outputData, outIdx, 4);
            outIdx += 4;

            outputData[outIdx] = 0x00; // left, down, right, up, options, R3, L3, share
            outputData[++outIdx] = 0x00; // square, cross, circle, triangle, r1, l1, r2, l2
            if (lmb) outputData[outIdx] |= 0x80;
            if (rmb) outputData[outIdx] |= 0x40;

            outputData[++outIdx] = 0x00; // PS
            outputData[++outIdx] = 0x00; // Touch

            outputData[++outIdx] = 0x00; // position left x
            outputData[++outIdx] = 0x00; // position left y
            outputData[++outIdx] = 0x00; // position right x
            outputData[++outIdx] = 0x00; // position right y

            outputData[++outIdx] = 0x00; // dpad left
            outputData[++outIdx] = 0x00; // dpad down
            outputData[++outIdx] = 0x00; // dpad right
            outputData[++outIdx] = 0x00; // dpad up

            outputData[++outIdx] = lmb ? (byte)0xFF : (byte)0; // square
            outputData[++outIdx] = rmb ? (byte)0xFF : (byte)0; ; // cross
            outputData[++outIdx] = 0x00; // circle
            outputData[++outIdx] = 0x00; // triange

            outputData[++outIdx] = 0x00; // r1
            outputData[++outIdx] = 0x00; // l1

            outputData[++outIdx] = 0x00; // r2
            outputData[++outIdx] = 0x00; // l2

            outIdx++;

            //DS4 only: touchpad points
            for (int i = 0; i < 2; i++)
            {
                outIdx += 6;
            }

            //motion timestamp
            Array.Copy(BitConverter.GetBytes(motionTimestamp), 0, outputData, outIdx, 8);
            outIdx += 8;

            //accelerometer
            Array.Copy(BitConverter.GetBytes(accelerometer.Y), 0, outputData, outIdx, 4);
            outIdx += 4;
            Array.Copy(BitConverter.GetBytes(-accelerometer.Z), 0, outputData, outIdx, 4);
            outIdx += 4;
            Array.Copy(BitConverter.GetBytes(accelerometer.X), 0, outputData, outIdx, 4);
            outIdx += 4;


            //gyroscope
            Array.Copy(BitConverter.GetBytes(gyro.X), 0, outputData, outIdx, 4); 
            outIdx += 4;
            Array.Copy(BitConverter.GetBytes(gyro.Y), 0, outputData, outIdx, 4);          
            outIdx += 4;
            Array.Copy(BitConverter.GetBytes(gyro.Z), 0, outputData, outIdx, 4);
            
            //outIdx += 4;

            FinishPacket(outputData);

            try { udpSock.SendTo(outputData, connectedClient); currentRotation += gyro;} catch (SocketException) { }

            //System.Diagnostics.Debug.WriteLine(currentRotation / 63);
        }
    }
}
