using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PupilManager : Singleton<PupilManager> {

    [Serializable]
    private class Settings
    {
        public List<PupilClient> pupil_clients = new List<PupilClient>();
        public string setup_id = "";
    }

    [Serializable]
    public class PupilClient
    {
        public string name;
        public string ip;
        public string port;
        public string surface_name;
        public bool detect_surface = true;
        public bool initially_active = true;
    }

    private Settings settings;
    private List<PupilListener> listeners = new List<PupilListener>();

    void Start()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "pupil_config.json");
        if (File.Exists(filePath))
        {
            string dataAsJson = File.ReadAllText(filePath);
            settings = JsonUtility.FromJson<Settings>(dataAsJson);
            Debug.Log(settings.setup_id + " have been loaded");

            StartListen();

        } else
        {
            Debug.Log("Config file does not exist");
        }
    }

    private void StartListen()
    {
        GameObject listener = new GameObject("PupilListener");
        PupilListener p = listener.AddComponent<PupilListener>();
        List<PupilClient> clients = new List<PupilClient>(settings.pupil_clients);
        clients.RemoveAll((c) => !c.initially_active);
        p.clients = clients;
        p.Listen();
    }
}
