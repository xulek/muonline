using UnityEngine;
using Client.Data.BMD;

public class BMDAnimator : MonoBehaviour
{
    public BMD bmd;
    public int actionIndex = 0;

    public float animationSpeed = 1f;

    private float currentTime = 0f;
    private int currentFrame = 0;

    private float baseFrameDuration = 1f / 15f;

    private SkinnedMeshRenderer skinnedMeshRenderer;
    private Mesh mesh;

    void Start()
    {
        skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

        if (skinnedMeshRenderer == null)
        {
            Debug.LogError("SkinnedMeshRenderer component not found in children!");
            enabled = false;
            return;
        }

        mesh = skinnedMeshRenderer.sharedMesh;

        if (bmd == null)
        {
            Debug.LogError("BMD is not assigned!");
            enabled = false;
            return;
        }

        currentFrame = 0;
        ApplyAnimationFrame(currentFrame);
    }

    void Update()
    {
        if (bmd == null || bmd.Actions.Length == 0)
            return;

        BMDTextureAction action = bmd.Actions[actionIndex];

        float speed = animationSpeed * action.PlaySpeed;

        currentTime += Time.deltaTime * speed;

        if (currentTime >= baseFrameDuration)
        {
            currentFrame = (currentFrame + 1) % action.NumAnimationKeys;
            currentTime = 0f;

            ApplyAnimationFrame(currentFrame);
        }
    }

    void ApplyAnimationFrame(int frame)
    {
        if (bmd == null || bmd.Bones.Length == 0 || actionIndex >= bmd.Actions.Length)
            return;

        var action = bmd.Actions[actionIndex];
        var bonesData = bmd.Bones;

        var unityBones = skinnedMeshRenderer.bones;

        for (int i = 0; i < bonesData.Length; i++)
        {
            var boneData = bonesData[i];
            var boneTransform = unityBones[i];

            if (boneTransform == null)
                continue;

            if (actionIndex < 0 || actionIndex >= boneData.Matrixes.Length)
                continue;

            var boneMatrix = boneData.Matrixes[actionIndex];

            if (frame < 0 || frame >= boneMatrix.Quaternion.Length || frame >= boneMatrix.Position.Length)
                continue;

            Quaternion rot = boneMatrix.Quaternion[frame];
            Vector3 pos = boneMatrix.Position[frame];

            boneTransform.localRotation = rot;
            boneTransform.localPosition = pos;
        }
    }
}
