using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadingController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera loadingCamera;
    [SerializeField] private Canvas loadingCanvas;
    [SerializeField] private Image progressFill;

    [Header("Timing")]
    [SerializeField] private float minLoadingTime = 1.0f;
    [SerializeField] private float worldReadyTimeoutSeconds = 20f;

    private bool sawGameSceneLoad;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Game", System.StringComparison.Ordinal))
            return;

        sawGameSceneLoad = true;
        if (scene.IsValid() && scene.isLoaded)
            SceneManager.SetActiveScene(scene);
    }

    void Start()
    {
        sawGameSceneLoad = false;
        loadingCamera.gameObject.SetActive(true);
        loadingCanvas.gameObject.SetActive(true);

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        ResetGlobalRunState();
        StartCoroutine(LoadGameRoutine());
    }

    void ResetGlobalRunState()
    {
        PauseMenuController.PrepareForMainMenuTransition();
        ChunkedProceduralLevelGenerator.ResetWorldReady();
        WorldMapController.EnsureExists();

        if (GrassSim.Stats.WorldStats.Instance != null)
            GrassSim.Stats.WorldStats.Instance.ResetStats();
    }

    IEnumerator LoadGameRoutine()
    {
        float timer = 0f;

        AsyncOperation op = SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f || timer < minLoadingTime)
        {
            timer += Time.deltaTime;

            if (progressFill != null)
            {
                float p = Mathf.Clamp01(op.progress / 0.9f);
                progressFill.fillAmount = Mathf.Lerp(0f, 0.8f, p);
            }

            yield return null;
        }

        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone || sawGameSceneLoad);
        EnsureGameSceneIsActive();
        yield return WaitForWorldReady();

        if (progressFill != null)
            progressFill.fillAmount = 1f;

        yield return null;

        loadingCanvas.gameObject.SetActive(false);
        loadingCamera.gameObject.SetActive(false);
        yield return UnloadLoadingSceneIfNeeded();
    }

    private void EnsureGameSceneIsActive()
    {
        Scene gameScene = SceneManager.GetSceneByName("Game");
        if (!gameScene.IsValid() || !gameScene.isLoaded)
            return;

        SceneManager.SetActiveScene(gameScene);
    }

    private IEnumerator UnloadLoadingSceneIfNeeded()
    {
        Scene loadingScene = gameObject.scene;
        if (!loadingScene.IsValid() || !loadingScene.isLoaded)
            yield break;

        AsyncOperation unload = SceneManager.UnloadSceneAsync(loadingScene);
        if (unload != null)
            yield return unload;
    }

    private IEnumerator WaitForWorldReady()
    {
        if (ChunkedProceduralLevelGenerator.WorldReady)
            yield break;

        float timeout = Mathf.Max(0f, worldReadyTimeoutSeconds);
        if (timeout <= 0f)
        {
            yield return new WaitUntil(() => ChunkedProceduralLevelGenerator.WorldReady);
            yield break;
        }

        float elapsed = 0f;
        while (!ChunkedProceduralLevelGenerator.WorldReady && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!ChunkedProceduralLevelGenerator.WorldReady)
        {
            Debug.LogWarning(
                $"[Loading] WorldReady timeout after {timeout:0.0}s. Continuing to avoid black-screen lock."
            );
        }
    }
}
