using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Authentication;
using System;
using UnityEngine.Audio;
using UnityEngine.Android;

public class MainScript : MonoBehaviour
{
    public TMP_Text statusText;
    public TMP_Text statusInd;
    public TMP_Text volText;
    public TMP_Text micText;
    public TMP_Text dopplerText;
    public TMP_Dropdown micSelector;
    public Button connectButton;
    public TMP_InputField addressInput;

    public Slider volSlider;
    public Slider micSlider;
    public Slider dopplerSlider;

    public GameObject myself;

    public AudioMixer mixer;

    public List<AudioMixerGroup> audioMixerGroups;
    public List<PanelController> playerPanelControllers;

    private bool[] audioMixerAvailability;
    private bool[] panelAvailability;
    private Dictionary<string, AudioMixerGroup> inUseAudioMixers = new Dictionary<string, AudioMixerGroup>();
    private Dictionary<string, PanelController> inUsePanelControllers = new Dictionary<string, PanelController>();

    private Server server = null;
    private float lastPacketTime = 0;

    private Dictionary<string, GameObject> otherPlayers = new Dictionary<string, GameObject>();

    private string myname = "";
    private bool connected = false;
    private bool loggedIn = false;
    private bool isInLobby = true;
    private bool isMirror = false;
    private bool initializingSliders = false;

    private Vector3 lobbyLocation = new Vector3(500000f, 500000, 500000);
    private float BaseDopplerFactor = 0.002f;
    private float MaxDopplerDistance = 200f;
    private float MegaPitchOffset = -0.2f;
    private float SmallPitchOffset = 0.8f;

    private float DopplerFactor;

    enum PacketType
    {
        JOIN_ROOM = 0,
        LEAVE_ROOM = 1,
        SET_PLAYERS = 2,
        UPDATE_MODE = 3,
        POSITION_INFO = 4,
        PING = 5,
        PROBE = 6,
    }

    enum VectorKind
    {
        KIND_NORMAL_U8,
        KIND_POSITION_U16,
    }

    private void SetStatus(string status, bool isError = false)
    {
        statusText.SetText(status);
        if (isError) {
            statusText.color = Color.red;
        }
        else
        {
            statusText.color = Color.white;
        }
    }

    private void SetConnectedIndicator(bool connected)
    {
        if (connected)
        {
            statusInd.color = Color.green;
        }
        else
        {
            statusInd.color = Color.red;
        }
    }

    private void SetMixerGroupPitch(AudioMixerGroup group, float pitch)
    {
        int id = group.name[6] - '0';
        string name = "pitchShift" + id;
        mixer.SetFloat(name, pitch);
    }

    private void SetMixerGroupVolume(AudioMixerGroup group, float volume)
    {
        int id = group.name[6] - '0';
        string name = "volume" + id;
        mixer.SetFloat(name, volume);
    }
    static public string NameToDisplayName(string name)
    {
        var split = name.Split('_', 2);
        return split.Length > 1 ? split[1] : split[0];
    }

    static public string NameToUniqueID(string name)
    {
        var split = name.Split('_', 2);
        return split[0];
    }

    private Vector3 DeserializeVector3(byte[] packet, ref int index, VectorKind kind)
    {
        float x = 0, y = 0, z = 0;

        switch (kind)
        {
            case VectorKind.KIND_NORMAL_U8:
                x = ((sbyte)packet[index]) / 100f;
                y = ((sbyte)packet[index + 1]) / 100f;
                z = ((sbyte)packet[index + 2]) / 100f;
                index += 3;
                break;
            case VectorKind.KIND_POSITION_U16:
                x = BitConverter.ToInt16(packet, index) / 10f;
                y = BitConverter.ToInt16(packet, index + 2) / 10f;
                z = BitConverter.ToInt16(packet, index + 4) / 10f;
                index += 6;
                break;
        }

        return new Vector3((isMirror ? x : -x), y, z);
    }

    private GameObject GetPlayer(int playerId)
    {
        if (myself.GetComponent<PlayerInfo>().playerID == playerId)
            return myself;

        foreach (var player in otherPlayers)
        {
            if (player.Value.GetComponent<PlayerInfo>().playerID == playerId)
                return player.Value;
        }
        return null;
    }

    private void UpdateDoppler(GameObject other)
    {
        if (!isInLobby)
        {
            PlayerInfo pi = other.GetComponent<PlayerInfo>();
            AudioSource audioSource = other.GetComponent<AudioSource>();

            float distance = (other.transform.position - myself.transform.position).magnitude;
            float prevDistance = pi.previousDistance;
            pi.previousDistance = distance;

            float currTime = Time.time;
            float increment = (distance - prevDistance) / (currTime - pi.previousCalcTime);
            pi.previousCalcTime = currTime;

            if (Math.Abs(increment) < MaxDopplerDistance)
            {
                float offset = (pi.isMega ? MegaPitchOffset : (pi.isSmall ? SmallPitchOffset : 0f));
                if (audioSource.outputAudioMixerGroup != null)
                    SetMixerGroupPitch(audioSource.outputAudioMixerGroup, Math.Clamp(1f + (-increment * DopplerFactor) + offset, 0.5f, 2f));
            }
        }
    }

    async void InitializeAsync()
    {
        await UnityServices.InitializeAsync();
        await AuthenticationService.Instance.SignInAnonymouslyAsync();

        await VivoxService.Instance.InitializeAsync();

        foreach (var dev in VivoxService.Instance.AvailableOutputDevices)
        {
            if (dev.DeviceName.Contains("No Device"))
            {
                await VivoxService.Instance.SetActiveOutputDeviceAsync(dev);
            }
        }

        VivoxService.Instance.ParticipantAddedToChannel += VivoxParticipantJoined;
        VivoxService.Instance.ParticipantRemovedFromChannel += VivoxParticipantLeft;
        VivoxService.Instance.AvailableInputDevicesChanged += OnMicAvailableDevicesChanged;

        SetStatus("Disconnected");
        SetConnectedIndicator(false);

        string address = PlayerPrefs.GetString("address", "");
        int vol = PlayerPrefs.GetInt("vol", 100);
        int mic = PlayerPrefs.GetInt("mic", 100);
        int doppler = PlayerPrefs.GetInt("doppler", 100);

        initializingSliders = true;
        addressInput.text = address;
        volSlider.value = vol;
        micSlider.value = mic;
        dopplerSlider.value = doppler;
        OnVolumeChangedImpl(vol);
        OnMicVolumeChangedImpl(mic);
        OnDopplerValueChangedImpl(doppler);
        initializingSliders = false;

        OnMicAvailableDevicesChanged();
    }

    private void VivoxParticipantJoined(VivoxParticipant participant)
    {
        string participantName = participant.DisplayName;
        if (participantName == myname || !isInLobby)
        {
            return;
        }

        var gameObject = participant.CreateVivoxParticipantTap(participantName);
        gameObject.AddComponent<PlayerInfo>();

        gameObject.transform.position = lobbyLocation;
        gameObject.transform.up = new Vector3(0, 1, 0);
        gameObject.transform.forward = new Vector3(0, 0, 1);

        AudioSource source = gameObject.GetComponent<AudioSource>();
        source.pitch = 1;
        source.spatialBlend = 1;
        source.dopplerLevel = 0;
        source.minDistance = 15;
        source.maxDistance = 60;
        source.rolloffMode = AudioRolloffMode.Linear;

        AudioMixerGroup mixerGroup = null;
        for (int i = 0; i < audioMixerAvailability.Length; i++)
        {
            if (audioMixerAvailability[i])
            {
                audioMixerAvailability[i] = false;
                mixerGroup = audioMixerGroups[i];
                break;
            }
        }
        if (mixerGroup != null)
        {
            source.outputAudioMixerGroup = mixerGroup;
            SetMixerGroupPitch(mixerGroup, 1f);
            SetMixerGroupVolume(mixerGroup, 0f);

            inUseAudioMixers.Add(participantName, mixerGroup);
        }

        PanelController c = null;
        for (int i = 0; i < panelAvailability.Length; i++)
        {
            if (panelAvailability[i])
            {
                panelAvailability[i] = false;
                c = playerPanelControllers[i];
                break;
            }
        }
        if (c != null)
        {
            c.ResetState();
            c.SetUserName(participantName);
            c.SetDisplayName(NameToDisplayName(participantName));
            c.Activate(true);

            int vol = PlayerPrefs.GetInt("vol_" + NameToUniqueID(participantName), 100);
            c.SetSliderValue(vol);
            OnPlayerVolumeChangedImpl(participantName, vol);

            inUsePanelControllers.Add(participantName, c);
        }

        AudioReverbFilter rev = gameObject.AddComponent<AudioReverbFilter>();
        rev.reverbPreset = AudioReverbPreset.SewerPipe;
        rev.enabled = false;

        otherPlayers.Add(gameObject.name, gameObject);
    }

    private void VivoxParticipantLeft(VivoxParticipant participant)
    {
        string participantName = participant.DisplayName;
        if (participantName == myname) { return; }

        AudioMixerGroup group;
        if (inUseAudioMixers.TryGetValue(participantName, out group))
        {
            SetMixerGroupPitch(group, 1f);
            SetMixerGroupVolume(group, 0f);

            inUseAudioMixers.Remove(participantName);
            for (int i = 0; i < audioMixerGroups.Count; i++)
            {
                if (audioMixerGroups[i] == group)
                {
                    audioMixerAvailability[i] = true;
                    break;
                }
            }
        }
        PanelController c;
        if (inUsePanelControllers.TryGetValue(participantName, out c))
        {
            c.ResetState();
            c.Activate(false);

            inUsePanelControllers.Remove(participantName);
            for (int i = 0; i < playerPanelControllers.Count; i++)
            {
                if (playerPanelControllers[i] == c)
                {
                    panelAvailability[i] = true;
                    break;
                }
            }
        }

        otherPlayers.Remove(participantName);
    }

    private async void JoinRoomPacketHandler(byte[] packet)
    {
        if (packet.Length != 132)
            return;

        loggedIn = true;

        string name = Encoding.Default.GetString(packet, 4, 64).Replace("\0", string.Empty);
        string room = Encoding.Default.GetString(packet, 4 + 64, 64).Replace("\0", string.Empty);

        SetStatus("Connected: Racing as " + NameToDisplayName(name));

        myname = name;
        isInLobby = true;
        isMirror = false;

        LoginOptions options = new LoginOptions();
        options.DisplayName = name;

        await VivoxService.Instance.LoginAsync(options);
        await VivoxService.Instance.JoinGroupChannelAsync(room, ChatCapability.AudioOnly);
        OnMicDeviceChanged();

        myself.transform.position = lobbyLocation;
        myself.transform.up = new Vector3(0, 1, 0);
        myself.transform.forward = new Vector3(0, 0, 1);
    }

    private async void LeaveRoomPacketHandler(byte[] packet, bool updateStatus = true)
    {
        if (!loggedIn && packet.Length != 4)
            return;

        await VivoxService.Instance.LeaveAllChannelsAsync();
        await VivoxService.Instance.LogoutAsync();

        if (updateStatus) SetStatus("Connected: Waiting for race");

        foreach (var group in inUseAudioMixers)
        {
            SetMixerGroupPitch(group.Value, 1f);
            audioMixerGroups.Add(group.Value);
        }
        inUseAudioMixers.Clear();

        foreach (var c in inUsePanelControllers)
        {
            c.Value.ResetState();
            c.Value.Activate(false);
            playerPanelControllers.Add(c.Value);
        }
        inUsePanelControllers.Clear();

        loggedIn = false;

        otherPlayers.Clear();
    }

    private void SetPlayersPacketHandler(byte[] packet)
    {
        if (!loggedIn && packet.Length != 516)
            return;

        myself.GetComponent<PlayerInfo>().playerID = -1;
        foreach (var player in otherPlayers)
        {
            player.Value.GetComponent<PlayerInfo>().playerID = -1;
        }

        for (int i = 0; i < 8; i++)
        {
            string name = Encoding.Default.GetString(packet, 4 + (i * 64), 64).Replace("\0", string.Empty);

            GameObject obj = null;
            if (name == myname)
            {
                obj = myself;
            }
            else
            {
                if (!otherPlayers.TryGetValue(name, out obj)) obj = null;
            }

            if (obj != null)
            {
                PlayerInfo pi = obj.GetComponent<PlayerInfo>();
                pi.playerID = i;
            }
        }

    }

    private void UpdateModePacketHandler(byte[] packet)
    {
        if (!loggedIn && packet.Length != 6)
            return;

        byte mode = packet[4];
        byte mirror = packet[5];

        isMirror = mirror != 0;

        if (mode == 0)
        {
            isInLobby = true;

            myself.transform.position = lobbyLocation;
            myself.transform.up = new Vector3(0, 1, 0);
            myself.transform.forward = new Vector3(0, 0, 1);

            foreach (var player in otherPlayers)
            {
                player.Value.transform.position = lobbyLocation;

                AudioMixerGroup group = player.Value.GetComponent<AudioSource>().outputAudioMixerGroup;
                if (group != null) SetMixerGroupPitch(group, 1f);

                PlayerInfo pi = player.Value.GetComponent<PlayerInfo>();
                pi.previousDistance = 0;
                pi.isStar = pi.wasStar = pi.isSmall = pi.isMega = false;

                player.Value.GetComponent<AudioReverbFilter>().enabled = false;
            }
        } else
        {
            isInLobby = false;
        }
    }

    private void UpdatePositionPacketHandler(byte[] packet)
    {
        if (!loggedIn && packet.Length != 66)
            return;

        if (isInLobby)
            return;

        int index = 4;
        
        Vector3 myFwd = DeserializeVector3(packet, ref index, VectorKind.KIND_NORMAL_U8);
        Vector3 myUp = DeserializeVector3(packet, ref index, VectorKind.KIND_NORMAL_U8);
        myFwd.Normalize();
        myUp.Normalize();

        Vector3[] pos = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            pos[i] = DeserializeVector3(packet, ref index, VectorKind.KIND_POSITION_U16);
        }

        byte[] flags = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            flags[i] = packet[index++];
        }

        Quaternion myrot = new Quaternion();
        myrot.SetLookRotation(myFwd, myUp);
        myself.transform.rotation = myrot;

        for (int i = 0; i < 8; i++)
        {
            GameObject obj = GetPlayer(i);
            if (obj != null)
            {
                obj.transform.position = pos[i];
                if (obj != myself)
                {
                    UpdateDoppler(obj);

                    PlayerInfo pi = obj.GetComponent<PlayerInfo>();
                    pi.isSmall = (flags[i] & 2) != 0;
                    pi.isMega = (flags[i] & 4) != 0;
                    pi.isStar = (flags[i] & 1) != 0;
                    if (pi.isStar != pi.wasStar)
                    {
                        pi.wasStar = pi.isStar;
                        obj.GetComponent<AudioReverbFilter>().enabled = pi.isStar;
                    }
                }
            }
        }
    }

    private void OnDisconnect()
    {
        connected = false;

        byte[] buffer = new byte[4];
        LeaveRoomPacketHandler(buffer, false);

        server.Disconnect();

        connectButton.GetComponentInChildren<TMP_Text>().text = "Connect";
        SetStatus("Disconnected");
        SetConnectedIndicator(false);
    }

    private void OnServerError(string error)
    {
        SetStatus(error, true);
    }

    private void OnServerPacket(byte[] packet)
    {
        if (packet.Length < 4)
            return;

        PacketType packetType = (PacketType)packet[1];

        switch (packetType)
        {
            case PacketType.JOIN_ROOM:
                JoinRoomPacketHandler(packet); break;

            case PacketType.LEAVE_ROOM:
                LeaveRoomPacketHandler(packet); break;

            case PacketType.SET_PLAYERS:
                SetPlayersPacketHandler(packet); break;

            case PacketType.UPDATE_MODE:
                UpdateModePacketHandler(packet); break;

            case PacketType.POSITION_INFO:
                UpdatePositionPacketHandler(packet); break;

            case PacketType.PING:
                break;

        }

        lastPacketTime = Time.time;
    }

    private void OnVolumeChangedImpl(int val)
    {
        volText.SetText(val + "%");
        if (val < 100)
        {
            mixer.SetFloat("masterVolume", ((100f - val) / 100f) * -80f);
        }
        else
        {
            mixer.SetFloat("masterVolume", (val - 100f) / 200f * 20f);
        }
    }

    private void OnMicVolumeChangedImpl(int val)
    {
        micText.SetText(val + "%");
        VivoxService.Instance.SetInputDeviceVolume((int)((val / 2f) - 50f));
    }

    private void OnDopplerValueChangedImpl(int val)
    {
        dopplerText.SetText(val + "%");

        DopplerFactor = BaseDopplerFactor * (val / 100f);
    }

    private void OnPlayerVolumeChangedImpl(string name, int val)
    {
        AudioMixerGroup g;
        if (inUseAudioMixers.TryGetValue(name, out g))
        {
            if (val < 100)
            {
                SetMixerGroupVolume(g, ((100f - val) / 100f) * -80f);
            }
            else
            {
                SetMixerGroupVolume(g, (val - 100f) / 100f * 20f);
            }
        }
    }

    public void OnVolumeChanged()
    {
        if (initializingSliders)
            return;

        int val = (int)volSlider.value;
        PlayerPrefs.SetInt("vol", val);
        OnVolumeChangedImpl(val);
    }

    public void OnMicVolumeChanged()
    {
        if (initializingSliders)
            return;

        int val = (int)micSlider.value;
        PlayerPrefs.SetInt("mic", val);
        OnMicVolumeChangedImpl(val);
    }

    public void OnMicDeviceChanged()
    {
        string deviceName = micSelector.options[micSelector.value].text;
        VivoxInputDevice selectedDevice = null;
        foreach (var dev in VivoxService.Instance.AvailableInputDevices)
        {
            if (dev.DeviceName == deviceName)
            {
                selectedDevice = dev;
                break;
            }
        }
        if (selectedDevice != null)
        {
            PlayerPrefs.SetString("micDev", deviceName);
            VivoxService.Instance.SetActiveInputDeviceAsync(selectedDevice);
        }
    }

    public void OnMicAvailableDevicesChanged()
    {
        string savedDevice = PlayerPrefs.GetString("micDev", "");
        List<string> inputDevices = new List<string>();
        VivoxInputDevice selectedDevice = null;
        foreach (var dev in VivoxService.Instance.AvailableInputDevices)
        {
            if (dev.DeviceName == savedDevice)
            {
                selectedDevice = dev;
            }
            inputDevices.Add(dev.DeviceName);
        }
        if (selectedDevice == null)
        {
            selectedDevice = VivoxService.Instance.ActiveInputDevice;
        }
        micSelector.AddOptions(inputDevices);
        int dropdownValue = inputDevices.FindIndex(s => s == selectedDevice.DeviceName);
        micSelector.SetValueWithoutNotify(dropdownValue);
    }

    public void OnDopplerValueChanged()
    {
        if (initializingSliders)
            return;

        int val = (int)dopplerSlider.value;
        PlayerPrefs.SetInt("doppler", val);
        OnDopplerValueChangedImpl(val);
    }

    public void OnPlayerVolumeChanged(PanelController c)
    {
        string name = c.GetUserName();
        int val = (int)c.GetSliderValue();
        PlayerPrefs.SetInt("vol_" + NameToUniqueID(name), val);
        OnPlayerVolumeChangedImpl(name, val);
    }

    public void OnConnectButtonPressed()
    {
        if (connected)
        {
            OnDisconnect();
        } else {
            PlayerPrefs.SetString("address", addressInput.text);
            if (addressInput.text.Length != 0 && server.Connect(addressInput.text))
            {
                connectButton.GetComponentInChildren<TMP_Text>().text = "Disconnect";
                SetStatus("Connected: Waiting for race");
                SetConnectedIndicator(true);
                connected = true;
                lastPacketTime = Time.time;
            }
        }
    }

    void Start()
    {
        if (Application.platform == RuntimePlatform.Android)
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (!Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO"))
            {
                Permission.RequestUserPermission("android.permission.RECORD_AUDIO");
            }
            if (!Permission.HasUserAuthorizedPermission("android.permission.MODIFY_AUDIO_SETTINGS"))
            {
                Permission.RequestUserPermission("android.permission.MODIFY_AUDIO_SETTINGS");
            }
            if (!Permission.HasUserAuthorizedPermission("android.permission.ACCESS_NETWORK_STATE"))
            {
                Permission.RequestUserPermission("android.permission.ACCESS_NETWORK_STATE");
            }
            if (!Permission.HasUserAuthorizedPermission("android.permission.ACCESS_WIFI_STATE"))
            {
                Permission.RequestUserPermission("android.permission.ACCESS_WIFI_STATE");
            }
            if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"))
            {
                Permission.RequestUserPermission("android.permission.BLUETOOTH_CONNECT");
            }
        }

        server = this.gameObject.GetComponent<Server>();
        server.onReceive += OnServerPacket;
        server.onError += OnServerError;
        foreach (var panel in playerPanelControllers)
        {
            panel.onSlierValueChangedAction += OnPlayerVolumeChanged;
            panel.ResetState();
            panel.Activate(false);
        }

        audioMixerAvailability = new bool[audioMixerGroups.Count];
        for (int i = 0; i < audioMixerAvailability.Length; i++)
        {
            audioMixerAvailability[i] = true;
        }

        panelAvailability = new bool[playerPanelControllers.Count];
        for (int i = 0; i < panelAvailability.Length; i++)
        {
            panelAvailability[i] = true;
        }

        InitializeAsync();
    }

    void Update()
    {
        // Force leave if nothing is received for 15 seconds
        if (connected && (Time.time - lastPacketTime) > 15)
        {
            OnDisconnect();
        }
    }
}
