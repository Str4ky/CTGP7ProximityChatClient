using UnityEngine;

public class toggleSettings : MonoBehaviour
{
    public GameObject SettingsPanel;

    void Start()
    {
        SettingsPanel.SetActive(false);
    }

    public void OpenSettings()
    {
        SettingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        SettingsPanel.SetActive(false);
    }
}