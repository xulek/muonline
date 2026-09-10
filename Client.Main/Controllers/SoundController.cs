// File: SoundController.cs
using Microsoft.Xna.Framework.Audio;
using NLayer;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Client.Main.Controllers
{
    public class SoundController : IDisposable
    {
        public static SoundController Instance { get; private set; } = new SoundController();

        private Dictionary<string, SoundEffect> _soundEffectCache = new Dictionary<string, SoundEffect>();
        private SoundEffectInstance _activeBackgroundMusicInstance;
        private string _currentBackgroundMusicPath;
        private SoundEffectInstance _activeAmbientSoundInstance;
        private string _currentAmbientSoundPath;
        private HashSet<string> _failedPaths = new HashSet<string>();
        private readonly Dictionary<string, Task<SoundEffect>> _soundEffectLoadTasks = new Dictionary<string, Task<SoundEffect>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _soundCacheLock = new object();
        private int _backgroundMusicRequestId;
        private int _ambientSoundRequestId;
        private readonly SemaphoreSlim _soundLoadGate = new(2, 2);
        private int _cacheGeneration;
        private bool _disposed;

        private sealed class ManagedLoopData
        {
            public SoundEffectInstance Instance;
            public float BaseVolume;
        }

        private sealed class RecentPlayInfo
        {
            public double LastTimeMs;
            public float LastVolume;
        }

        private readonly Dictionary<string, ManagedLoopData> _managedLoopingInstances = new Dictionary<string, ManagedLoopData>();
        private ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger<SoundController>();
        private readonly Stopwatch _playStopwatch = Stopwatch.StartNew();
        private readonly Dictionary<string, RecentPlayInfo> _recentOneShots = new Dictionary<string, RecentPlayInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly object _recentOneShotsLock = new object();
        private const double OneShotMinIntervalMs = 120.0; // prevent identical bursts stacking on the same frame
        private const float OneShotVolumeSlack = 0.05f;   // allow slightly louder instances to break through the throttle

        private SoundController()
        {
            SoundEffect.MasterVolume = MathHelper.Clamp(Constants.SOUND_EFFECTS_VOLUME / 100f, 0f, 1f);
        }

        public void StopBackgroundMusic()
        {
            Interlocked.Increment(ref _backgroundMusicRequestId);
            StopBackgroundMusicInstance();
        }

        private void StopBackgroundMusicInstance()
        {
            _activeBackgroundMusicInstance?.Stop(true);
            _activeBackgroundMusicInstance?.Dispose();
            _activeBackgroundMusicInstance = null;
            _currentBackgroundMusicPath = null;
        }

        public void PlayBackgroundMusic(string relativePath)
        {
            if (!Constants.BACKGROUND_MUSIC || string.IsNullOrEmpty(relativePath))
            {
                StopBackgroundMusic();
                return;
            }

            if (_currentBackgroundMusicPath == relativePath &&
                _activeBackgroundMusicInstance != null &&
                !_activeBackgroundMusicInstance.IsDisposed &&
                _activeBackgroundMusicInstance.State == SoundState.Playing)
            {
                return;
            }

            var requestId = Interlocked.Increment(ref _backgroundMusicRequestId);
            StopBackgroundMusicInstance();

            _ = PlayBackgroundMusicAsync(relativePath, requestId);
        }

        public void PreloadBackgroundMusic(string relativePath)
        {
            if ((!Constants.BACKGROUND_MUSIC && !Constants.SOUND_EFFECTS) || string.IsNullOrEmpty(relativePath))
                return;

            _ = LoadSoundEffectDataAsync(Path.Combine(Constants.DataPath, relativePath));
        }

        public void StopAmbientSound()
        {
            Interlocked.Increment(ref _ambientSoundRequestId);
            StopAmbientSoundInstance();
        }

        private void StopAmbientSoundInstance()
        {
            _activeAmbientSoundInstance?.Stop(true);
            _activeAmbientSoundInstance?.Dispose();
            _activeAmbientSoundInstance = null;
            _currentAmbientSoundPath = null;
        }

        public void PlayAmbientSound(string relativePath)
        {
            if (!Constants.SOUND_EFFECTS || string.IsNullOrEmpty(relativePath))
            {
                StopAmbientSound();
                return;
            }

            if (_currentAmbientSoundPath == relativePath &&
                _activeAmbientSoundInstance != null &&
                !_activeAmbientSoundInstance.IsDisposed &&
                _activeAmbientSoundInstance.State == SoundState.Playing)
            {
                return;
            }

            int requestId = Interlocked.Increment(ref _ambientSoundRequestId);
            StopAmbientSoundInstance();
            _ = PlayAmbientSoundAsync(relativePath, requestId);
        }

        /// <summary>
        /// Preloads a sound effect into the cache to avoid loading delays during playback.
        /// This is a new, more generic method name.
        /// </summary>
        public void PreloadSound(string relativePath)
        {
            _ = PreloadSoundAsync(relativePath);
        }

        public async Task PreloadSoundAsync(string relativePath)
        {
            if ((!Constants.BACKGROUND_MUSIC && !Constants.SOUND_EFFECTS) || string.IsNullOrEmpty(relativePath))
                return;

            await LoadSoundEffectDataAsync(Path.Combine(Constants.DataPath, relativePath)).ConfigureAwait(false);
        }

        /// <summary>
        /// Plays a sound effect with volume attenuation based on distance.
        /// Can be looped if 'loop' parameter is true.
        /// </summary>
        public void PlayBufferWithAttenuation(string relativePath, Vector3 sourcePosition, Vector3 listenerPosition, float maxDistance = 1000f, bool loop = false)
        {
            if (!Constants.SOUND_EFFECTS || string.IsNullOrEmpty(relativePath)) return;

            string fullPath = Path.Combine(Constants.DataPath, relativePath);
            float distance = Vector3.Distance(sourcePosition, listenerPosition);
            float volume = 1.0f - (distance / maxDistance);
            volume = MathHelper.Clamp(volume, 0f, 1f);
            float fxFactor = Constants.SOUND_EFFECTS_VOLUME / 100f;

            if (loop)
            {
                string instanceKey = fullPath.ToLowerInvariant();
                if (!_managedLoopingInstances.TryGetValue(instanceKey, out var managed) || managed.Instance == null || managed.Instance.IsDisposed)
                {
                    SoundEffect sfxData = GetCachedOrBeginLoad(fullPath);
                    if (sfxData == null || sfxData.IsDisposed)
                        return;

                    var newInstance = sfxData.CreateInstance();
                    newInstance.IsLooped = true;
                    managed = new ManagedLoopData
                    {
                        Instance = newInstance,
                        BaseVolume = 0f
                    };
                    _managedLoopingInstances[instanceKey] = managed;
                }

                var instance = managed.Instance;
                managed.BaseVolume = MathHelper.Clamp(volume, 0f, 1f);
                UpdateManagedLoopVolume(managed);

                if (managed.BaseVolume > 0.01f)
                {
                    if (instance.State != SoundState.Playing)
                    {
                        try
                        {
                            instance.Play();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug($"[ManagedLoop] Error playing instance of '{relativePath}': {ex.Message}");
                            instance.Dispose();
                            _managedLoopingInstances.Remove(instanceKey);
                        }
                    }
                    else
                    {
                        UpdateManagedLoopVolume(managed);
                    }
                }
                else if (instance.State == SoundState.Playing)
                {
                    instance.Pause();
                }
            }
            else
            {
                float scaledVolume = MathHelper.Clamp(volume * fxFactor, 0f, 1f);
                if (scaledVolume <= 0.01f || fxFactor <= 0f)
                {
                    return;
                }

                PlayOneShot(fullPath, volume, throttle: true);
            }
        }

        public void PlayBuffer(string relativePath)
        {
            if (!Constants.SOUND_EFFECTS || string.IsNullOrEmpty(relativePath)) return;

            string fullPath = Path.Combine(Constants.DataPath, relativePath);
            PlayOneShot(fullPath, 1f, throttle: false);
        }

        private void PlayOneShot(string fullPath, float attenuation, bool throttle)
        {
            if (_disposed || Constants.SOUND_EFFECTS_VOLUME <= 0) return;
            SoundEffect cached = GetCachedSoundEffect(fullPath.ToLowerInvariant());
            if (cached != null)
            {
                PlayLoadedOneShot(cached, fullPath, attenuation, throttle);
                return;
            }

            // A cache miss must retain the play request, not just warm the next cast.
            _ = PlayOneShotAfterLoadAsync(fullPath, attenuation, throttle, Volatile.Read(ref _cacheGeneration));
        }

        private async Task PlayOneShotAfterLoadAsync(string fullPath, float attenuation, bool throttle, int generation)
        {
            try
            {
                SoundEffect sound = await LoadSoundEffectDataAsync(fullPath).ConfigureAwait(false);
                if (sound == null) return;
                MuGame.ScheduleOnMainThread(() =>
                {
                    if (!_disposed && generation == Volatile.Read(ref _cacheGeneration))
                        PlayLoadedOneShot(sound, fullPath, attenuation, throttle);
                }, MainThreadDispatcher.WorkPriority.High, "SoundController.PlayLoadedOneShot");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Unable to load one-shot sound {Path}", fullPath);
            }
        }

        private void PlayLoadedOneShot(SoundEffect sound, string fullPath, float attenuation, bool throttle)
        {
            if (_disposed || !Constants.SOUND_EFFECTS || sound.IsDisposed) return;
            float volume = MathHelper.Clamp(attenuation * Constants.SOUND_EFFECTS_VOLUME / 100f, 0f, 1f);
            if (volume <= 0f || (throttle && ShouldThrottleOneShot(fullPath, volume))) return;
            try
            {
                sound.Play(volume, 0f, 0f);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Unable to play one-shot sound {Path}", fullPath);
            }
        }

        public void ApplyBackgroundMusicVolume()
        {
            if (_activeBackgroundMusicInstance != null && !_activeBackgroundMusicInstance.IsDisposed)
            {
                var rawVolume = Constants.BACKGROUND_MUSIC_VOLUME / 100f;
                if (rawVolume <= 0f)
                {
                    StopBackgroundMusic();
                    return;
                }

                var volume = MathHelper.Clamp(rawVolume, 0f, 1f);

                if (float.IsNaN(volume) || float.IsInfinity(volume))
                {
                    volume = 1f;
                }

                volume = Math.Max(0f, Math.Min(1f, volume));

                _activeBackgroundMusicInstance.Volume = volume;
            }
        }

        public void ApplySoundEffectsVolume()
        {
            float master = Constants.SOUND_EFFECTS ? MathHelper.Clamp(Constants.SOUND_EFFECTS_VOLUME / 100f, 0f, 1f) : 0f;
            SoundEffect.MasterVolume = master;
            foreach (var entry in _managedLoopingInstances.Values)
            {
                UpdateManagedLoopVolume(entry);
            }
        }

        private void UpdateManagedLoopVolume(ManagedLoopData managed)
        {
            if (managed?.Instance == null || managed.Instance.IsDisposed)
            {
                return;
            }

            if (!Constants.SOUND_EFFECTS)
            {
                managed.Instance.Volume = 0f;
                if (managed.Instance.State == SoundState.Playing)
                {
                    managed.Instance.Pause();
                }
                return;
            }

            float factor = Constants.SOUND_EFFECTS_VOLUME / 100f;
            var calculatedVolume = managed.BaseVolume * factor;
            managed.Instance.Volume = MathHelper.Clamp(calculatedVolume, 0f, 1f);
            if (managed.Instance.Volume <= 0f)
            {
                if (managed.Instance.State == SoundState.Playing)
                {
                    managed.Instance.Pause();
                }
            }
            else if (managed.Instance.State != SoundState.Playing)
            {
                try
                {
                    managed.Instance.Play();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"[ManagedLoop] Error resuming loop instance: {ex.Message}");
                }
            }
        }

        private SoundEffect GetCachedOrBeginLoad(string fullPath)
        {
            string cacheKey = fullPath.ToLowerInvariant();
            SoundEffect cached = GetCachedSoundEffect(cacheKey);
            if (cached != null)
                return cached;

            if (!IsFailedPath(cacheKey))
                _ = LoadSoundEffectDataAsync(fullPath);

            return null;
        }

        private SoundEffect LoadSoundEffectData(string fullPath)
        {
            string cacheKey = fullPath.ToLowerInvariant();
            if (IsFailedPath(cacheKey)) return null;

            if (!File.Exists(fullPath))
            {
                _logger?.LogDebug($"[LoadSoundEffectData] File not found: {fullPath}");
                MarkFailedPath(cacheKey);
                return null;
            }

            string extension = Path.GetExtension(fullPath)?.ToLowerInvariant();
            try
            {
                SoundEffect loadedSfx = null;
                if (extension == ".wav")
                {
                    loadedSfx = SoundEffect.FromFile(fullPath);
                }
                else if (extension == ".mp3")
                {
                    byte[] pcmData = LoadMp3PcmData(fullPath, out int sampleRate, out int channels);
                    if (pcmData != null && sampleRate > 0 && channels > 0 && pcmData.Length > 0)
                    {
                        loadedSfx = new SoundEffect(pcmData, sampleRate, (AudioChannels)channels);
                    }
                    else
                    {
                        _logger?.LogDebug($"[LoadSoundEffectData] Failed to load PCM data from MP3: {fullPath}");
                        MarkFailedPath(cacheKey);
                    }
                }
                else
                {
                    _logger?.LogDebug($"[LoadSoundEffectData] Unsupported audio file extension: {extension} for path {fullPath}");
                    MarkFailedPath(cacheKey);
                    return null;
                }

                return loadedSfx;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[LoadSoundEffectData] Error loading sound data from '{fullPath}': {ex.Message}");
                MarkFailedPath(cacheKey);
            }
            return null;
        }

        private byte[] LoadMp3PcmData(string filePath, out int sampleRate, out int channels)
        {
            sampleRate = 0;
            channels = 0;
            try
            {
                using var fs = File.OpenRead(filePath);
                using var mpegFile = new MpegFile(fs);
                if (mpegFile.SampleRate == 0 || mpegFile.Channels == 0 || mpegFile.Length <= 0)
                {
                    _logger?.LogDebug("[LoadMp3PcmData] Invalid or empty MP3: {Path}", filePath);
                    return null;
                }

                sampleRate = mpegFile.SampleRate;
                channels = mpegFile.Channels;
                int floatBufferLength = 1152 * channels * 2;
                float[] floatBuffer = ArrayPool<float>.Shared.Rent(floatBufferLength);
                byte[] pcmBuffer = ArrayPool<byte>.Shared.Rent(floatBufferLength * sizeof(short));
                try
                {
                    using var output = new MemoryStream();
                    int samplesRead;
                    while ((samplesRead = mpegFile.ReadSamples(floatBuffer, 0, floatBufferLength)) > 0)
                    {
                        int byteCount = samplesRead * sizeof(short);
                        for (int i = 0; i < samplesRead; i++)
                        {
                            short sample = (short)(MathHelper.Clamp(floatBuffer[i], -1f, 1f) * short.MaxValue);
                            int offset = i * 2;
                            pcmBuffer[offset] = (byte)(sample & 0xFF);
                            pcmBuffer[offset + 1] = (byte)((sample >> 8) & 0xFF);
                        }

                        output.Write(pcmBuffer, 0, byteCount);
                    }

                    return output.Length > 0 ? output.ToArray() : null;
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(floatBuffer);
                    ArrayPool<byte>.Shared.Return(pcmBuffer);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[LoadMp3PcmData] Error loading MP3 file {Path}", filePath);
                return null;
            }
        }

        public void ClearSoundCaches()
        {
            Interlocked.Increment(ref _cacheGeneration);
            StopBackgroundMusic();
            StopAmbientSound();

            List<SoundEffect> cached;
            lock (_soundCacheLock)
            {
                cached = new List<SoundEffect>(_soundEffectCache.Values);
                _soundEffectCache.Clear();
                _soundEffectLoadTasks.Clear();
                _failedPaths.Clear();
            }

            foreach (var sfx in cached)
            {
                sfx?.Dispose();
            }

            foreach (var instanceEntry in _managedLoopingInstances)
            {
                var managed = instanceEntry.Value;
                if (managed?.Instance != null)
                {
                    managed.Instance.Stop(true);
                    managed.Instance.Dispose();
                    managed.Instance = null;
                }
            }
            _managedLoopingInstances.Clear();
            lock (_recentOneShotsLock)
                _recentOneShots.Clear();

            _logger?.LogDebug("SoundController: SoundEffect cache and managed looping instances cleared.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearSoundCaches();
        }

        private async Task PlayAmbientSoundAsync(string relativePath, int requestId)
        {
            SoundEffect ambientEffect;
            try
            {
                ambientEffect = await LoadSoundEffectDataAsync(Path.Combine(Constants.DataPath, relativePath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[PlayAmbientSound] Failed loading {Path}", relativePath);
                return;
            }

            if (ambientEffect == null)
                return;

            MuGame.ScheduleOnMainThread(() =>
            {
                if (requestId != Volatile.Read(ref _ambientSoundRequestId) || !Constants.SOUND_EFFECTS)
                    return;

                StopAmbientSoundInstance();
                try
                {
                    _activeAmbientSoundInstance = ambientEffect.CreateInstance();
                    _activeAmbientSoundInstance.IsLooped = true;
                    _activeAmbientSoundInstance.Volume = 1f;
                    _activeAmbientSoundInstance.Play();
                    _currentAmbientSoundPath = relativePath;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "[PlayAmbientSound] Error playing {Path}", relativePath);
                    StopAmbientSoundInstance();
                }
            }, MainThreadDispatcher.WorkPriority.High);
        }

        private async Task PlayBackgroundMusicAsync(string relativePath, int requestId)
        {
            SoundEffect musicEffect = null;
            var fullPath = Path.Combine(Constants.DataPath, relativePath);
            try
            {
                musicEffect = await LoadSoundEffectDataAsync(fullPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"[PlayBackgroundMusic] Error loading sound '{relativePath}': {ex.Message}");
            }

            if (musicEffect == null)
            {
                _logger?.LogDebug($"[PlayBackgroundMusic] Failed to load SoundEffect for: {relativePath}");
                return;
            }

            MuGame.ScheduleOnMainThread(() =>
            {
                if (requestId != _backgroundMusicRequestId || !Constants.BACKGROUND_MUSIC)
                {
                    return;
                }

                _activeBackgroundMusicInstance?.Stop(true);
                _activeBackgroundMusicInstance?.Dispose();
                _activeBackgroundMusicInstance = musicEffect.CreateInstance();
                _activeBackgroundMusicInstance.IsLooped = true;
                try
                {
                    ApplyBackgroundMusicVolume();
                    _activeBackgroundMusicInstance.Play();
                    _currentBackgroundMusicPath = relativePath;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"[PlayBackgroundMusic] Error playing sound '{relativePath}': {ex.Message}");
                    StopBackgroundMusicInstance();
                }
            }, MainThreadDispatcher.WorkPriority.High);
        }

        private Task<SoundEffect> LoadSoundEffectDataAsync(string fullPath)
        {
            if (_disposed)
                return Task.FromResult<SoundEffect>(null);

            string cacheKey = fullPath.ToLowerInvariant();
            SoundEffect cached = GetCachedSoundEffect(cacheKey);
            if (cached != null)
                return Task.FromResult(cached);

            if (IsFailedPath(cacheKey))
                return Task.FromResult<SoundEffect>(null);

            lock (_soundCacheLock)
            {
                if (_soundEffectLoadTasks.TryGetValue(cacheKey, out var existingTask))
                    return existingTask;

                int generation = Volatile.Read(ref _cacheGeneration);
                Task<SoundEffect> task = LoadSoundEffectDataBoundedAsync(fullPath, cacheKey, generation);
                _soundEffectLoadTasks[cacheKey] = task;
                return task;
            }
        }

        private async Task<SoundEffect> LoadSoundEffectDataBoundedAsync(string fullPath, string cacheKey, int generation)
        {
            await _soundLoadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                SoundEffect effect = await Task.Run(() => LoadSoundEffectData(fullPath)).ConfigureAwait(false);
                if (effect == null)
                    return null;

                if (_disposed || generation != Volatile.Read(ref _cacheGeneration))
                {
                    effect.Dispose();
                    return null;
                }

                CacheSoundEffect(cacheKey, effect);
                return effect;
            }
            finally
            {
                _soundLoadGate.Release();
                if (generation == Volatile.Read(ref _cacheGeneration))
                {
                    lock (_soundCacheLock)
                        _soundEffectLoadTasks.Remove(cacheKey);
                }
            }
        }

        private SoundEffect GetCachedSoundEffect(string cacheKey)
        {
            lock (_soundCacheLock)
            {
                if (_soundEffectCache.TryGetValue(cacheKey, out var sfx))
                {
                    if (sfx != null && !sfx.IsDisposed)
                    {
                        return sfx;
                    }
                    _soundEffectCache.Remove(cacheKey);
                }
            }

            return null;
        }

        private void CacheSoundEffect(string cacheKey, SoundEffect effect)
        {
            lock (_soundCacheLock)
            {
                _soundEffectCache[cacheKey] = effect;
            }
        }

        private bool IsFailedPath(string cacheKey)
        {
            lock (_soundCacheLock)
            {
                return _failedPaths.Contains(cacheKey);
            }
        }

        private void MarkFailedPath(string cacheKey)
        {
            lock (_soundCacheLock)
            {
                _failedPaths.Add(cacheKey);
            }
        }

        private bool ShouldThrottleOneShot(string cacheKey, float finalVolume)
        {
            var nowMs = _playStopwatch.Elapsed.TotalMilliseconds;
            lock (_recentOneShotsLock)
            {
                if (_recentOneShots.TryGetValue(cacheKey, out var info))
                {
                    var elapsed = nowMs - info.LastTimeMs;
                    bool isQuieterOrEqual = finalVolume <= info.LastVolume + OneShotVolumeSlack;
                    if (elapsed < OneShotMinIntervalMs && isQuieterOrEqual)
                    {
                        return true;
                    }

                    info.LastTimeMs = nowMs;
                    info.LastVolume = Math.Max(finalVolume, info.LastVolume);
                }
                else
                {
                    _recentOneShots[cacheKey] = new RecentPlayInfo
                    {
                        LastTimeMs = nowMs,
                        LastVolume = finalVolume
                    };
                }
            }

            return false;
        }
    }
}
