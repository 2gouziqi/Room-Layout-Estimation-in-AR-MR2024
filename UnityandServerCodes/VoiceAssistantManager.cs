using MixedReality.Toolkit.UX;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VoiceAssistantManager : MonoBehaviour
{
    public GameObject[] buttons;
    private AudioSource[] audioSources;
    private PressableButton[] toggles;

    void Start()
    {
        // Get all AudioSource components attached to this GameObject
        audioSources = GetComponents<AudioSource>();
        

        for (int i = 0; i < buttons.Length; i++)
        {
            GameObject button = buttons[i];
            toggles[i] = button.GetComponent<PressableButton>(); ;
        }
    }

    void Update()
    {
        // Loop through all audio sources and check if any are not playing
        for (int i = 0; i < audioSources.Length; i++)
        {
            // If the audio source is not playing
            if (!audioSources[i].isPlaying)
            {
                toggles[i].ForceSetToggled(false);
            }
        }
    }

    // Method to play a specific audio clip
    public void PlayDialogue(int audioSourceIndex)
    {
        if (audioSources[audioSourceIndex].isPlaying == true)
        {
            StopAllAudio();
        }
        else
        {
            StopAllAudio();
            audioSources[audioSourceIndex].Play();
        }
    }

    // Method to stop all audio sources (if needed)
    public void StopAllAudio()
    {
        foreach (var audioSource in audioSources)
        {
            audioSource.Stop();
        }
    }
}