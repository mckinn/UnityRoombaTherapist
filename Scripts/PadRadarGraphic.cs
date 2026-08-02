using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class PadRadarGraphic : Graphic
{
    [SerializeField] private float radius = 140f;
    [SerializeField] private int segments = 64;
    [SerializeField] private Color centerColor = Color.red;
    [SerializeField] private Color edgeColor = Color.green;
    [SerializeField] private float lineThickness = 2f;
    [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.4f);

    // programmatic label positioning
    [SerializeField] private RectTransform labelArousal;
    [SerializeField] private RectTransform labelDominance;
    [SerializeField] private RectTransform labelPleasure;
    [SerializeField] private float labelGap = 15f;

    // [SerializeField] private float examplePleasure = 0.4f;
    // [SerializeField] private float exampleArousal = -0.5f;
    // [SerializeField] private float exampleDominance = 0.7f;
    [SerializeField] private float triangleThickness = 3f;
    [SerializeField] private Color triangleColor = Color.white;  

    private static readonly float[] axisAngles = { 0f, 120f, 240f }; // A, D, P (degrees, clockwise from up)


    protected override void OnPopulateMesh(VertexHelper vh)
    {
        Debug.Log("OnPopulateMesh called");  
        vh.Clear();

        Vector2 center = rectTransform.rect.center;

        UIVertex centerVert = UIVertex.simpleVert;
        centerVert.color = centerColor;
        centerVert.position = center;
        vh.AddVert(centerVert);

        for (int i = 0; i <= segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            UIVertex vert = UIVertex.simpleVert;
            vert.color = edgeColor;
            vert.position = pos;
            vh.AddVert(vert);
        }

        for (int i = 1; i <= segments; i++)
        {
            vh.AddTriangle(0, i, i + 1);
        }

        // new: axis guideline lines
        foreach (float angleDeg in axisAngles)
        {
            Vector2 direction = AngleToDirection(angleDeg);
            Vector2 lineEnd = center + direction * ( radius * 1.1f );
            AddLine(vh, center, lineEnd, lineThickness, lineColor);
        }

        PADState pad = (SessionManager.Instance != null  && SessionManager.Instance.CurrentPad != null )
            ? SessionManager.Instance.CurrentPad 
            : new PADState();

        Vector2 arousalPoint = GetAxisPoint(center, 0f, pad.arousal);
        Vector2 dominancePoint = GetAxisPoint(center, 120f, pad.dominance);
        Vector2 pleasurePoint = GetAxisPoint(center, 240f, pad.pleasure);
        // Vector2 arousalPoint = GetAxisPoint(center, 0f, exampleArousal);
        // Vector2 dominancePoint = GetAxisPoint(center, 120f, exampleDominance);
        // Vector2 pleasurePoint = GetAxisPoint(center, 240f, examplePleasure);

        AddLine(vh, arousalPoint, dominancePoint, triangleThickness, triangleColor);
        AddLine(vh, dominancePoint, pleasurePoint, triangleThickness, triangleColor);
        AddLine(vh, pleasurePoint, arousalPoint, triangleThickness, triangleColor);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Debug.Log("PadRadarGraphic OnEnable");
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnStateUpdated += HandleStateUpdated;
            HandleStateUpdated(); // pick up whatever state already exists, don't wait for the next change
            Debug.Log("PadRadarGraphic OnStateUpdated added");
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Debug.Log("PadRadarGraphic OnDisable");
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnStateUpdated -= HandleStateUpdated;
        }
    }  

    private void HandleStateUpdated()
    {
        Debug.Log("in HandleStateUpdated");
        SetVerticesDirty();
    }

    private Vector2 AngleToDirection(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    private void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color lineColor)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x) * (thickness / 2f);

        int startIndex = vh.currentVertCount;

        AddVertAt(vh, start - perpendicular, lineColor);
        AddVertAt(vh, start + perpendicular, lineColor);
        AddVertAt(vh, end + perpendicular, lineColor);
        AddVertAt(vh, end - perpendicular, lineColor);

        vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
        vh.AddTriangle(startIndex, startIndex + 2, startIndex + 3);
    }

    private void AddVertAt(VertexHelper vh, Vector2 position, Color vertColor)
    {
        UIVertex v = UIVertex.simpleVert;
        v.color = vertColor;
        v.position = position;
        vh.AddVert(v);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        RepositionLabels();
    }

    private void RepositionLabels()
    {
        if (labelArousal == null || labelDominance == null || labelPleasure == null)
        {
            return;
        }

        Vector2 center = rectTransform.rect.center;
        float labelDistance = radius * 1.1f + labelGap;

        labelArousal.anchoredPosition = center + AngleToDirection(0f) * labelDistance;
        labelDominance.anchoredPosition = center + AngleToDirection(120f) * labelDistance;
        labelPleasure.anchoredPosition = center + AngleToDirection(240f) * labelDistance;
    }

    private Vector2 GetAxisPoint(Vector2 center, float angleDegrees, float rawValue)
    {
        float normalized = (rawValue + 1f) / 2f; // -1..1 -> 0..1
        Vector2 direction = AngleToDirection(angleDegrees);
        return center + direction * (radius * normalized);
    }
}