using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using GrassSim.Core;

public class MainMenuIntroController : MonoBehaviour
{
    [Header("Roots")]
    public GameObject introRoot;
    public GameObject mainMenuRoot;

    [Header("Intro Elements")]
    public Image blackBackground;
    public Image sorontarLogo;
    public Image gameLogo;

    [Header("Timing")]
    public float logoFadeTime = 1.0f;
    public float logoHoldTime = 1.2f;

    [Header("Scene Fade")]
    public Image sceneFadeOverlay;
    public float sceneFadeTime = 1.2f;

    [Header("Menu Fade")]
    public CanvasGroup mainMenuGroup;
    public float menuFadeTime = 0.8f;


    void Awake()
    {
        GameSettings.Load();
    }
    
    void Start()
    {
        Time.timeScale = 1f;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // 🔒 scena czarna
        SetAlpha(sceneFadeOverlay, 1f);

        // 🔒 menu niewidoczne
        mainMenuRoot.SetActive(true);
        mainMenuGroup.alpha = 0f;
        mainMenuGroup.interactable = false;
        mainMenuGroup.blocksRaycasts = false;

        introRoot.SetActive(true);

        StartCoroutine(IntroSequence());
    }

    IEnumerator IntroSequence()
    {
        // stan początkowy intro
        SetAlpha(blackBackground, 1f);
        SetAlpha(sorontarLogo, 0f);
        SetAlpha(gameLogo, 0f);

        // 1️⃣ Sorontar logo
        yield return Fade(sorontarLogo, 0f, 1f, logoFadeTime);
        yield return new WaitForSecondsRealtime(logoHoldTime);
        yield return Fade(sorontarLogo, 1f, 0f, logoFadeTime);

        // 2️⃣ Game logo
        yield return Fade(gameLogo, 0f, 1f, logoFadeTime);
        yield return new WaitForSecondsRealtime(logoHoldTime);
        yield return Fade(gameLogo, 1f, 0f, logoFadeTime);

        // 3️⃣ TERAZ DOPIERO ODSŁANIAMY SCENĘ
        mainMenuRoot.SetActive(true);
        // 3️⃣ ODSŁANIAMY SCENĘ
        yield return Fade(sceneFadeOverlay, 1f, 0f, sceneFadeTime);

        // 4️⃣ CHOWAMY INTRO
        introRoot.SetActive(false);

        // 5️⃣ FADE-IN MENU
        yield return FadeMenuIn();

        // 6️⃣ KURSOR
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    IEnumerator Fade(Image img, float from, float to, float time)
    {
        float t = 0f;
        Color c = img.color;

        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(from, to, t / time);
            img.color = c;
            yield return null;
        }

        c.a = to;
        img.color = c;
    }

    void SetAlpha(Image img, float a)
    {
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    IEnumerator FadeMenuIn()
    {
        float t = 0f;

        while (t < menuFadeTime)
        {
            t += Time.unscaledDeltaTime;
            mainMenuGroup.alpha = Mathf.Lerp(0f, 1f, t / menuFadeTime);
            yield return null;
        }

        mainMenuGroup.alpha = 1f;
        mainMenuGroup.interactable = true;
        mainMenuGroup.blocksRaycasts = true;
    }
}
