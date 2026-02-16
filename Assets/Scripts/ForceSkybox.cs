using UnityEngine;

public class ForceSkybox : MonoBehaviour
{
    public Material skybox;

    void Awake()
    {
        if (skybox == null)
        {
            Debug.LogError("[ForceSkybox] Skybox material is NULL");
            return;
        }

        RenderSettings.skybox = skybox;
        DynamicGI.UpdateEnvironment();

        Debug.Log("[ForceSkybox] Skybox forced: " + skybox.name);
    }
}
