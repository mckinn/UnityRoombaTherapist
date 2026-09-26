using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class EndBannerController : MonoBehaviour
{
    [Tooltip("Fires OnTimeExpired once when the level timer runs out - for presenting the 'you ran out of power' message")]
    [SerializeField] private GameLevelTimer gameLevelTimer;

    [Tooltip("Fires OnExitReached once when the Roomba passes through the unlocked egress door - for presenting the 'enjoy the next level' message")]
    [SerializeField] private EgressExitDetector egressExitDetector;

    [Tooltip("Fires OnExitReached once when the Roomba passes through the unlocked egress door - for presenting the 'enjoy the next level' message")]
    [SerializeField] private GameScoreTable gameScoreTable;

    [Tooltip("Find the GameObject where you want the display of the end of the world results")]
    [SerializeField] private GameObject freezeFrame;

    private bool bannerDisplayed = false;

    private string roombaName = SessionManager.Instance.name;

    private void OnEnable()
    {
        Debug.Log($"ShowNoPowerBanner: in OnEnable");
        if (gameLevelTimer != null)
        {
            Debug.Log($"ShowNoPowerBanner: adding ShowNoPower");
            gameLevelTimer.OnTimeExpired += ShowNoPowerBanner;
        }
        if (egressExitDetector != null)
        {
            Debug.Log($"ShowNoPowerBanner: adding ShowNextLevel");
            egressExitDetector.OnExitReached += ShowNextLevelBanner;
        }
    }

    private void OnDisable()
    {
        if (gameLevelTimer != null)
        {
            gameLevelTimer.OnTimeExpired -= ShowNoPowerBanner;
        }
        if (egressExitDetector != null)
        {
            egressExitDetector.OnExitReached -= ShowNextLevelBanner;
        }
    }

    /// <summary>
    /// prioritize getting out of the room over running out of time
    /// </summary>
    private void ShowNextLevelBanner()
    {
        FillBanner($"Congrats on the new level {roombaName}",
                   $"Well Done!  You can now spend time with your Therapist to determine how to improve if you want.\nYour overall score was {gameScoreTable.overallScore:P0}.\n<shift><Escape> to reset the game\"");
    }

    private void ShowNoPowerBanner()
    {
        FillBanner($"No Power - try again {roombaName}",
                   $"I'm sorry to see that you ran out of power.\nFocus on getting your speed up - you can consult with your Therapist now to figure out how.\nYour overall score was {gameScoreTable.overallScore:P0}.\n<shift><Escape> to reset the game");
    }

    private void FillBanner(string header, string information)
    {
        
        Debug.Log($"ShowNoPowerBanner: in FillBanner");
        if (!bannerDisplayed)
        {
            freezeFrame.SetActive(true);
            TMP_Text[] displays = freezeFrame.GetComponentsInChildren<TMP_Text>();
            Debug.Log($"ShowNoPowerBanner: {header}, {information}");
            foreach (TMP_Text displayText in displays )
            {
                Debug.Log($"ShowNoPowerBanner: textfield name {displayText.name}");
                switch (displayText.name) {
                    case "Header":
                        displayText.text = header;
                        break;
                    case "Details":
                        displayText.text = information;
                        break;
                    default:
                        Debug.Log($"ShowNoPowerBanner: invalid field name {displayText.name}");
                        break;
                }
            }
            bannerDisplayed = true;
        }
    }

}
