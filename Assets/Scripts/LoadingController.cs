using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class LoadingController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera loadingCamera;
    [SerializeField] private Canvas loadingCanvas;
    [SerializeField] private Image progressFill;

    [Header("Timing")]
    [SerializeField] private float minLoadingTime = 1.0f;

    void Start()
    {
        // 🔒 START: loading zawsze aktywny (stara logika zostaje)
        loadingCamera.gameObject.SetActive(true);
        loadingCanvas.gameObject.SetActive(true);

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        // ✅ NOWE: reset stanu runu (DODANE, reszty nie ruszamy)
        ResetGlobalRunState();

        StartCoroutine(LoadGameRoutine());
    }

    void ResetGlobalRunState()
    {
        // reset flag świata, żeby WaitUntil nie przeskoczył
        ChunkedProceduralLevelGenerator.ResetWorldReady();

        // reset timera
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.ResetTimer();

        // reset statów
        if (GrassSim.Stats.WorldStats.Instance != null)
            GrassSim.Stats.WorldStats.Instance.ResetStats();

        // (opcjonalnie) usuń muzykę gameplay, jeśli bywa DontDestroy i zostaje po runie
        var music = Object.FindAnyObjectByType<MusicPhaseController>();
        if (music != null)
            Destroy(music.gameObject);
    }

    IEnumerator LoadGameRoutine()
    {
        float timer = 0f;

        // 1️⃣ ŁADUJEMY GAME ADDITIVE (stara logika zostaje)
        AsyncOperation op = SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f || timer < minLoadingTime)
        {
            timer += Time.deltaTime;

            if (progressFill != null)
            {
                // 0–0.9 → 0–0.8 paska
                float p = Mathf.Clamp01(op.progress / 0.9f);
                progressFill.fillAmount = Mathf.Lerp(0f, 0.8f, p);
            }

            yield return null;
        }

        // 2️⃣ AKTYWUJ SCENĘ (stara logika zostaje)
        op.allowSceneActivation = true;

        // 3️⃣ CZEKAJ NA GENERACJĘ ŚWIATA (stara logika zostaje)
        yield return new WaitUntil(() => ChunkedProceduralLevelGenerator.WorldReady);

        // 4️⃣ DOMKNIJ PASEK (stara logika zostaje)
        if (progressFill != null)
            progressFill.fillAmount = 1f;

        yield return null; // 1 klatka buforu

        // 5️⃣ ATOMOWO WYŁĄCZ LOADING (stara logika zostaje)
        loadingCanvas.gameObject.SetActive(false);
        loadingCamera.gameObject.SetActive(false);
    }
}
