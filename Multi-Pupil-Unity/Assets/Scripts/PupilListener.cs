using UnityEngine;
using System;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Generic;
using System.Net;

namespace Pupil
{
    [Serializable]
    public class ProjectedSphere
    {
        public double[] axes = new double[] { 0, 0 };
        public double angle;
        public double[] center = new double[] { 0, 0 };
    }
    [Serializable]
    public class Sphere
    {
        public double radius;
        public double[] center = new double[] { 0, 0, 0 };
    }
    [Serializable]
    public class Circle3d
    {
        public double radius;
        public double[] center = new double[] { 0, 0, 0 };
        public double[] normal = new double[] { 0, 0, 0 };
    }
    [Serializable]
    public class Ellipse
    {
        public double[] axes = new double[] { 0, 0 };
        public double angle;
        public double[] center = new double[] { 0, 0 };
    }
    [Serializable]
    public class PupilData3D
    {
        public double diameter;
        public double confidence;
        public ProjectedSphere projected_sphere = new ProjectedSphere();
        public double theta;
        public int model_id;
        public double timestamp;
        public double model_confidence;
        public string method;
        public double phi;
        public Sphere sphere = new Sphere();
        public double diameter_3d;
        public double[] norm_pos = new double[] { 0, 0, 0 };
        public int id;
        public double model_birth_timestamp;
        public Circle3d circle_3d = new Circle3d();
        public Ellipse ellipese = new Ellipse();
    }
    [Serializable]
    public class GazeOnSurface
    {
        public bool on_srf;
        public string topic;
        public double confidence;
        public double[] norm_pos = new double[] { 0, 0, 0 };
    }
    [Serializable]
    public class SurfaceData3D
    {
        public double timestamp;
        public double uid;
        public String name;
        public List<GazeOnSurface> gaze_on_srf = new List<GazeOnSurface>();
    }
}

public class PupilListener : MonoBehaviour
{
    Thread client_thread_;
    private System.Object thisLock_ = new System.Object();
    bool stop_thread_ = false;

    public List<PupilManager.PupilClient> clients = new List<PupilManager.PupilClient>();
    private List<String> IPHeaders = new List<string>();
    private List<SubscriberSocket> subscriberSockets = new List<SubscriberSocket>();

    private bool isConnected = true;
    private int turn = 0;

    Pupil.PupilData3D pupilData = new Pupil.PupilData3D();
    Pupil.SurfaceData3D surfaceData = new Pupil.SurfaceData3D();

    public void get_transform(ref Vector3 pos, ref Quaternion q)
    {
        lock (thisLock_)
        {
            pos = new Vector3(
                        (float)(pupilData.sphere.center[0]),
                        (float)(pupilData.sphere.center[1]),
                        (float)(pupilData.sphere.center[2])
                        ) * 0.001f;// in [m]
            q = Quaternion.LookRotation(new Vector3(
            (float)(pupilData.circle_3d.normal[0]),
            (float)(pupilData.circle_3d.normal[1]),
            (float)(pupilData.circle_3d.normal[2])
            ));
        }
    }

    public void Listen()
    {
        client_thread_ = new Thread(NetMQClient);
        client_thread_.Start();
    }


    void NetMQClient()
    {
        var timeout = new System.TimeSpan(0, 0, 1);
        AsyncIO.ForceDotNet.Force();
        NetMQConfig.ManualTerminationTakeOver();
        NetMQConfig.ContextCreate(true);

        List<string> subports = new List<string>();     // subports for each client connection

        // loop through all clients and try to connect to them
        foreach (PupilManager.PupilClient c in clients)
        {
            string subport = "";
            string IPHeader = ">tcp://" + c.ip + ":";
            bool frameReceived = false;

            Debug.LogFormat("Requesting socket for {0}:{1} ({2})", c.ip, c.port, c.name);
            RequestSocket requestSocket;
            try
            {
                // validate ip header
                if (!validateIPHeader(c.ip, c.port))
                {
                    Debug.LogErrorFormat("{0}:{1} is not a valid ip header for client {2}", c.ip, c.port, c.name);
                    continue;
                }
                
                requestSocket = new RequestSocket(IPHeader + c.port);
                if (requestSocket != null)
                {
                    requestSocket.SendFrame("SUB_PORT");
                    timeout = new System.TimeSpan(0, 0, 1);
                    frameReceived = requestSocket.TryReceiveFrameString(timeout, out subport);  // request subport, will be saved in var subport for this client

                    if (frameReceived)
                    {
                        subports.Add(subport);
                        IPHeaders.Add(IPHeader);
                    }
                    else
                    {
                        Debug.LogErrorFormat("Could not connect to client {0}:{1} ({2}). Make sure address is corect and pupil remote service is running", c.ip, c.port ,c.name);
                    }
                    requestSocket.Close();
                }

            } catch (Exception e)
            {
                Debug.LogErrorFormat("Could not reach to client {0}:{1} ({2}): {4}", c.ip, c.port, c.name, e.ToString());
            }

        }

        isConnected = (IPHeaders.Count == clients.Count);   // check if all clients are connected
        
        if (isConnected)
        {
            Debug.LogFormat("Connected to {0} sockets", IPHeaders.Count);
            foreach (String header in IPHeaders)
            {
                SubscriberSocket subscriberSocket = new SubscriberSocket(header + subports[IPHeaders.IndexOf(header)]);
                if (clients[IPHeaders.IndexOf(header)].detect_surface)
                {
                    subscriberSocket.Subscribe("surface");
                }
                subscriberSocket.Subscribe("pupil.");
                subscriberSockets.Add(subscriberSocket);
            }

            var msg = new NetMQMessage();
            turn = 0;   // used receive a message from each client in turn
            while (!stop_thread_)
            {
                turn = ++turn % IPHeaders.Count;
                timeout = new System.TimeSpan(0, 0, 0, 0, 200);     // wait 200ms to receive a message

                bool stillAlive = subscriberSockets[turn].TryReceiveMultipartMessage(timeout, ref (msg));

                if (stillAlive)
                {
                    try
                    {
                        string msgType = msg[0].ConvertToString();

                        var message = MsgPack.Unpacking.UnpackObject(msg[1].ToByteArray());

                        MsgPack.MessagePackObject mmap = message.Value;
                        if (msgType.Contains("pupil"))
                        {
                            // pupil detected
                            lock (thisLock_)
                            {
                                pupilData = JsonUtility.FromJson<Pupil.PupilData3D>(mmap.ToString());
                            }
                        }

                        if (msgType == "surfaces")
                        {
                            // surface detected
                            lock (thisLock_)
                            {
                                surfaceData = JsonUtility.FromJson<Pupil.SurfaceData3D>(mmap.ToString());
                            }
                        }
                    }
                    catch
                    {
                        Debug.LogWarningFormat("Failed to deserialize pupil data for client {0}", clients[turn].name);
                    }
                }
            }
            foreach (SubscriberSocket s in subscriberSockets)
            {
                s.Close();
            }
            subscriberSockets.Clear();
        }
        else
        {
            Debug.LogWarning("Failed to connect to all clients specified in config file");
        }
        NetMQConfig.ContextTerminate();
    }

    private Boolean validateIPHeader(string ip, string port)
    {
        if (ip.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Length == 4)
        {
            IPAddress ipAddr;
            if (IPAddress.TryParse(ip, out ipAddr))
            {
                if (int.Parse(port) <= 65535)
                {
                    return true;
                }
            }
        }
        return false;
    }

    void OnApplicationQuit()
    {
        lock (thisLock_)stop_thread_ = true;
        foreach (SubscriberSocket s in subscriberSockets)
        {
            s.Close();
        }
        NetMQConfig.ContextTerminate();
        Debug.Log("Network Threads terminated.");
    }
}