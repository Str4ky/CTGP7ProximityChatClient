using UnityEngine;

public class toggleSettings : MonoBehaviour
{
    public GameObject SettingsPanel;

    public void OpenSettings()
    {
        SettingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        SettingsPanel.SetActive(false);
    }
}