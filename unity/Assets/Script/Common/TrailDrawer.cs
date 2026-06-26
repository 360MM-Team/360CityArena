using UnityEngine;

public class TrailDrawer
{
    public RenderTexture trailRT;
    private Material lineMat;

    public TrailDrawer(int width, int height, RenderTexture mapRenderTexture)
    {
        // RenderTexture for the trajectory.
        trailRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        // trailRT = mapRenderTexture;
        trailRT.Create();

        // Initialize as transparent once.
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = trailRT;
        GL.Clear(true, true, new Color(0, 0, 0, 0));
        RenderTexture.active = prev;

        // Simple material for transparent lines.
        lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        lineMat.hideFlags = HideFlags.HideAndDontSave;
        lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMat.SetInt("_ZWrite", 0);
    }

    // Draw a straight line using UV coordinates in the 0 to 1 range.
    public void DrawLine(Vector2 startUV, Vector2 endUV, Color color, float width)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = trailRT;

        GL.PushMatrix();
        GL.LoadPixelMatrix(0, trailRT.width, 0, trailRT.height);

        lineMat.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(color);

        // Convert UV to pixel coordinates.
        Vector2 a = new Vector2(startUV.x * trailRT.width, startUV.y * trailRT.height);
        Vector2 b = new Vector2(endUV.x * trailRT.width, endUV.y * trailRT.height);
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * width * 0.5f;

        // Draw a thick line as a rectangle.
        GL.Vertex(a - n);
        GL.Vertex(a + n);
        GL.Vertex(b + n);
        GL.Vertex(b - n);

        GL.End();
        GL.PopMatrix();

        RenderTexture.active = prev;
    }
}
