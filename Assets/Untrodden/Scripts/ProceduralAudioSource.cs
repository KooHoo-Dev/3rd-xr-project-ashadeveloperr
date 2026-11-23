using UnityEngine;

namespace Untrodden
{
    /// <summary>AudioSource에 ProceduralAudio로 만든 임시 클립을 넣는다.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralAudioSource : MonoBehaviour
    {
        public enum Kind { Wind, Crackle, Knock, Beep }
        public Kind kind;

        void Awake()
        {
            var src = GetComponent<AudioSource>();
            src.clip = kind switch
            {
                Kind.Wind => ProceduralAudio.Wind(),
                Kind.Crackle => ProceduralAudio.Crackle(),
                Kind.Knock => ProceduralAudio.Knock(),
                _ => ProceduralAudio.Beep(),
            };
            if (src.playOnAwake && src.enabled) src.Play();
        }
    }
}
