using System.IO;
using UnityEngine;

namespace ARES.EditorTools
{
    /// <summary>
    /// 창을 열지 않고 모델(과 애니메이션의 한 시점)을 그림으로 굽는 진입점.
    /// 확인용 그림을 파일로 남기거나, 다른 에디터 도구·자동 확인에서 모델이 어떻게 보이는지 볼 때 쓴다.
    /// 그리는 방식은 모델 뷰어 창과 같다 (<see cref="ModelViewerStage"/>). 클립은 제자리 재생으로 굽는다.
    /// </summary>
    public static class ModelViewerCapture
    {
        /// <summary>
        /// 모델을 그림 한 장으로 굽는다. clip 이 있으면 time 초 시점의 자세로 굽는다.
        /// degreesFromFront: 0 = 앞, 90 = 왼쪽 옆, 180 = 뒤. 돌려받은 그림은 쓴 쪽이 지운다.
        /// </summary>
        public static Texture2D Render(GameObject model, AnimationClip clip = null, float time = 0f,
            int width = 512, int height = 512, float degreesFromFront = 25f)
        {
            if (model == null) return null;
            using (var stage = new ModelViewerStage())
            {
                stage.SetModel(model, false);
                stage.SetHelpers(true, false, 1.8f);
                stage.LookFrom(degreesFromFront);
                if (clip != null) stage.Sample(clip, time);
                return stage.Capture(width, height, true);
            }
        }

        /// <summary>
        /// 클립을 같은 간격의 여러 시점으로 나눠 가로로 이어 붙인 그림을 굽는다. 동작이 모델에 맞는지 한눈에 보는 용도.
        /// </summary>
        public static Texture2D RenderStrip(GameObject model, AnimationClip clip, int frames = 6,
            int frameWidth = 320, int frameHeight = 400, float degreesFromFront = 25f)
        {
            if (model == null || clip == null) return null;
            frames = Mathf.Clamp(frames, 1, 32);
            var strip = new Texture2D(frameWidth * frames, frameHeight, TextureFormat.RGB24, false);
            using (var stage = new ModelViewerStage())
            {
                stage.SetModel(model, false);
                stage.SetHelpers(true, false, 1.8f);
                stage.LookFrom(degreesFromFront);
                for (int i = 0; i < frames; i++)
                {
                    stage.Sample(clip, clip.length * i / frames);
                    Texture2D frame = stage.Capture(frameWidth, frameHeight, true);
                    strip.SetPixels32(i * frameWidth, 0, frameWidth, frameHeight, frame.GetPixels32());
                    Object.DestroyImmediate(frame);
                }
            }
            strip.Apply();
            return strip;
        }

        /// <summary>그림을 PNG 파일로 쓴다. 폴더가 없으면 만든다.</summary>
        public static void SavePng(Texture2D texture, string filePath)
        {
            if (texture == null || string.IsNullOrEmpty(filePath)) return;
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(filePath, texture.EncodeToPNG());
        }
    }
}
