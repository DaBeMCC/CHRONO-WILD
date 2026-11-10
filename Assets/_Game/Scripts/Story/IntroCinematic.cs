using ChronoWild.Player;
using ChronoWild.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ChronoWild.Story
{
    /// <summary>
    /// The opening of the game in two parts: a pre-rendered video of the time machine's arrival
    /// (streamed from StreamingAssets so it also works in WebGL), then the in-engine crash-site
    /// intro on a separate cinematic camera (Cinemachine shots driven by a Timeline). Afterwards
    /// control goes to the player. Space or Enter skips the current part.
    /// </summary>
    public class IntroCinematic : MonoBehaviour
    {
        public PlayableDirector director;
        public Camera cinematicCamera;
        public Camera playerCamera;
        public DebugFirstPersonController controller;
        public bool playOnStart = true;
        [Tooltip("Time of day the story begins at.")]
        public float startHour = 7.2f;

        [Header("Cinematic quality (only while the in-engine shots play)")]
        [Tooltip("Shadows reach the far side of the island, so wide shots keep their depth.")]
        public float cinematicShadowDistance = 450f;
        [Tooltip("Distant trees keep their detailed models.")]
        public float cinematicLodBias = 4f;

        [Header("Arrival video")]
        public VideoPlayer video;
        [Tooltip("File inside StreamingAssets. Leave empty to skip the video.")]
        public string videoFile = "Intro.webm";
        [Tooltip("The video's soundtrack, played separately so it stays in sync however the frames advance.")]
        public AudioSource soundtrack;
        [Tooltip("Give up on the video if it is not ready after this many seconds.")]
        public float videoTimeout = 8f;

        /// <summary>True while the intro is on screen (the mission HUD hides meanwhile).</summary>
        public static bool Playing { get; private set; }

        // Seen once per session: restarting after a game over goes straight back to the beach.
        static bool seen;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { seen = false; Playing = false; }

        enum Phase { None, Video, Timeline }
        Phase phase;
        int savedCullingMask;
        float videoWait;
        bool videoStarted;
        bool manualStepping;
        float videoClock;
        long lastFrame = -1;
        long steppedFrom = long.MinValue;
        int smoothFrames;
        float settleStart, lastProgress, seekAt;
        bool pendingSeek;
        long seekFrom;
        GameObject videoScreen;
        RenderTexture videoTexture;

        void Start()
        {
            var clock = DayNightCycle.Instance;
            if (clock != null)
                clock.SetHour(startHour);
            if (playOnStart && !seen)
                Play();
            else
                Finish(seen);
        }

        public void Play()
        {
            Playing = true;
            controller.enabled = false;
            playerCamera.enabled = false;
            cinematicCamera.gameObject.SetActive(true);
            if (video != null && !string.IsNullOrEmpty(videoFile))
                PlayVideo();
            else
                PlayTimeline();
        }

        void PlayVideo()
        {
            phase = Phase.Video;
            videoWait = 0f;
            videoStarted = manualStepping = false;
            videoClock = 0f;
            lastFrame = -1;
            smoothFrames = 0;
            settleStart = Time.realtimeSinceStartup;
            // The video covers the whole screen, so the camera behind it does not need to draw the world.
            savedCullingMask = cinematicCamera.cullingMask;
            cinematicCamera.cullingMask = 0;
            cinematicCamera.clearFlags = CameraClearFlags.SolidColor;
            cinematicCamera.backgroundColor = Color.black;
            video.source = VideoSource.Url;
            video.url = System.IO.Path.Combine(Application.streamingAssetsPath, videoFile);
            // Drawn on a full-screen overlay through a render texture: it shows the same on every
            // platform, whatever the camera stack is doing.
            if (videoScreen == null)
                BuildVideoScreen();
            videoScreen.SetActive(true);
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = videoTexture;
            video.aspectRatio = VideoAspectRatio.FitInside;
            video.isLooping = false;
            video.playOnAwake = false;
            video.audioOutputMode = soundtrack != null ? VideoAudioOutputMode.None : VideoAudioOutputMode.Direct;
            video.errorReceived -= OnVideoError;
            video.errorReceived += OnVideoError;
            video.prepareCompleted -= OnVideoPrepared;
            video.prepareCompleted += OnVideoPrepared;
            video.Prepare();
        }

        void BuildVideoScreen()
        {
            videoTexture = new RenderTexture(1280, 720, 0) { name = "Intro Video" };
            videoScreen = new GameObject("Intro Video Screen");
            videoScreen.transform.SetParent(transform, false);
            var canvas = videoScreen.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var backdrop = videoScreen.AddComponent<Image>();
            backdrop.color = Color.black;
            var picture = new GameObject("Picture", typeof(RectTransform));
            picture.transform.SetParent(videoScreen.transform, false);
            var rt = (RectTransform)picture.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            picture.AddComponent<RawImage>().texture = videoTexture;
            var fitter = picture.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
        }

        void OnVideoPrepared(VideoPlayer v)
        {
            // Playback starts from UpdateVideo once the scene has settled.
        }

        void StartPlayback(VideoPlayer v)
        {
            if (phase != Phase.Video || videoStarted)
                return;
            videoStarted = true;
            videoWait = 0f;
            lastProgress = Time.realtimeSinceStartup;
            pendingSeek = false;
            steppedFrom = long.MinValue;
            // The Linux editor (and possibly other desktop players) decodes frames but does not run
            // the playback clock reliably, so outside the browser the frames are stepped by hand,
            // following the soundtrack. Browsers play the video natively.
            manualStepping = v.canStep && Application.platform != RuntimePlatform.WebGLPlayer;
            if (manualStepping)
                v.Pause();
            else
                v.Play();
            if (soundtrack != null)
                soundtrack.Play();
        }

        void OnVideoError(VideoPlayer v, string message)
        {
            Debug.LogWarning($"Intro video could not play ({message}); skipping to the in-engine intro.");
            EndVideo();
        }

        void EndVideo()
        {
            if (phase != Phase.Video)
                return;
            phase = Phase.None;
            video.Stop();
            if (soundtrack != null)
                soundtrack.Stop();
            if (videoScreen != null)
                videoScreen.SetActive(false);
            if (videoTexture != null)
                videoTexture.Release();
            cinematicCamera.cullingMask = savedCullingMask;
            cinematicCamera.clearFlags = CameraClearFlags.Skybox;
            PlayTimeline();
        }

        /// <summary>
        /// Keeps the video moving. Some platforms (the Linux editor among them) decode the frames
        /// but never advance the playback clock; there the frames are stepped by hand on our own
        /// clock. Anything that cannot play at all falls through to the in-engine intro.
        /// </summary>
        void UpdateVideo()
        {
            // Clamped so the long first frames after loading the scene do not count.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!videoStarted)
            {
                // Hold on black until the first heavy frames after loading the scene are over, so
                // the video does not start (and stutter) while the world is still being set up.
                smoothFrames = Time.unscaledDeltaTime < 0.05f ? smoothFrames + 1 : 0;
                bool settled = smoothFrames >= 20 || Time.realtimeSinceStartup - settleStart > 8f;
                if (video.isPrepared && settled)
                    StartPlayback(video);
                else if (!video.isPrepared && (videoWait += dt) > videoTimeout)
                    OnVideoError(video, "timed out while loading");
                return;
            }
            bool sound = soundtrack != null && soundtrack.isPlaying;
            if (manualStepping)
            {
                // The soundtrack is the clock; the picture follows it.
                videoClock = sound ? soundtrack.time : videoClock + dt;
                long target = (long)(videoClock * video.frameRate);
                long frame = video.frame;
                if (pendingSeek)
                {
                    // A seek takes a few frames to land; asking again would cancel it.
                    if (frame != seekFrom || Time.realtimeSinceStartup - seekAt > 1f)
                        pendingSeek = false;
                }
                else if (target - frame > 12)
                {
                    pendingSeek = true;
                    seekFrom = frame;
                    seekAt = Time.realtimeSinceStartup;
                    video.frame = target;
                }
                else if (frame < target && frame != steppedFrom)
                {
                    // One step at a time: wait for each step to land before the next.
                    steppedFrom = frame;
                    video.StepForward();
                }
            }
            else
            {
                // Native playback: the picture is the clock and the soundtrack follows it.
                videoClock = (float)video.time;
                if (sound && video.time > 0.05 && Mathf.Abs(soundtrack.time - videoClock) > 0.12f)
                    soundtrack.time = Mathf.Min(videoClock, soundtrack.clip.length - 0.05f);
                if (video.frame <= 0 && Time.realtimeSinceStartup - lastProgress > 2.5f && video.canStep)
                {
                    manualStepping = true;   // the clock is not running: take over
                    video.Pause();
                }
            }
            // Stalled for good (no new frame for a while): give up rather than leave a frozen screen.
            if (video.frame != lastFrame)
            {
                lastFrame = video.frame;
                lastProgress = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - lastProgress > 6f)
            {
                OnVideoError(video, "playback stalled");
                return;
            }
            float soundEnd = soundtrack != null && soundtrack.clip != null ? soundtrack.clip.length - 0.05f : float.MaxValue;
            if (videoClock >= video.length || videoClock >= soundEnd || (video.frameCount > 0 && (ulong)(video.frame + 1) >= video.frameCount))
                EndVideo();
        }

        float savedShadowDistance = -1f, savedLodBias, savedPixelError, savedBasemap;

        /// <summary>
        /// Wide establishing shots see the island from far away, beyond the gameplay shadow and
        /// LOD distances; for the length of the cinematic those are raised, then put back.
        /// </summary>
        void SetCinematicQuality(bool on)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var terrain = Terrain.activeTerrain;
            if (on && savedShadowDistance < 0f)
            {
                if (urp != null) { savedShadowDistance = urp.shadowDistance; urp.shadowDistance = Mathf.Max(urp.shadowDistance, cinematicShadowDistance); }
                else savedShadowDistance = 0f;
                savedLodBias = QualitySettings.lodBias;
                QualitySettings.lodBias = Mathf.Max(savedLodBias, cinematicLodBias);
                if (terrain != null)
                {
                    savedPixelError = terrain.heightmapPixelError; savedBasemap = terrain.basemapDistance;
                    terrain.heightmapPixelError = 1f; terrain.basemapDistance = 1000f;
                }
            }
            else if (!on && savedShadowDistance >= 0f)
            {
                if (urp != null) urp.shadowDistance = savedShadowDistance;
                QualitySettings.lodBias = savedLodBias;
                if (terrain != null) { terrain.heightmapPixelError = savedPixelError; terrain.basemapDistance = savedBasemap; }
                savedShadowDistance = -1f;
            }
        }

        void PlayTimeline()
        {
            phase = Phase.Timeline;
            SetCinematicQuality(true);
            director.stopped -= OnStopped;
            director.stopped += OnStopped;
            director.time = 0;
            director.Play();
        }

        void OnStopped(PlayableDirector d) => Finish();

        void OnDestroy()
        {
            SetCinematicQuality(false);
            Playing = false;
            if (director != null)
                director.stopped -= OnStopped;
            if (videoTexture != null)
                videoTexture.Release();
        }

        void Finish() => Finish(false);

        void Finish(bool quiet)
        {
            SetCinematicQuality(false);
            seen = true;
            Playing = false;
            phase = Phase.None;
            // Leaving play mode also stops the director, when the cameras may already be gone.
            if (cinematicCamera == null || playerCamera == null || controller == null)
                return;
            cinematicCamera.gameObject.SetActive(false);
            playerCamera.enabled = true;
            controller.enabled = true;
            var mission = MissionDirector.Instance;
            if (mission != null && !quiet)
                mission.ShowMessage("Diario de la expedición, día 1. El salto falló y la máquina quedó destrozada. Sus piezas y nuestras provisiones están repartidas por la isla. Sigue la luz.", 10f, mission.introVoice);
        }

        void Update()
        {
            if (phase == Phase.None)
                return;
            if (phase == Phase.Video)
                UpdateVideo();
            if (phase == Phase.None)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                if (phase == Phase.Video)
                    EndVideo();
                else
                    director.Stop();
            }
        }
    }
}
