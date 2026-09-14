using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Presentation-only camera controller for the render-texture viewport.
/// It never changes drone state or telemetry.
public class DroneViewportCameraController : MonoBehaviour
{
    public enum ViewMode { Follow, Orbit, Top, Free }

    public Transform target;
    public ViewMode mode = ViewMode.Follow;
    public float distance = 9f;
    public float height = 3f;
    public float topHeight = 12f;
    public float positionDamping = 9f;
    public float rotationDamping = 10f;

    float orbitYaw = 35f;
    float orbitPitch = 18f;

    public void Initialize(Transform followTarget)
    {
        target = followTarget;
        if (target != null && transform.IsChildOf(target)) transform.SetParent(null, true);
        SetMode(ViewMode.Follow);
    }

    public void SetMode(ViewMode nextMode)
    {
        mode = nextMode;
        if (mode == ViewMode.Orbit && target != null)
        {
            Vector3 offset = transform.position - target.position;
            distance = Mathf.Clamp(offset.magnitude, 4f, 18f);
            if (offset.sqrMagnitude > 0.01f)
            {
                orbitYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                orbitPitch = Mathf.Clamp(Mathf.Asin(offset.y / offset.magnitude) * Mathf.Rad2Deg, 8f, 70f);
            }
        }
    }

    public void ResetView()
    {
        distance = 9f;
        height = 3f;
        orbitYaw = 35f;
        orbitPitch = 18f;
        SetMode(ViewMode.Follow);
    }

    public void Drag(Vector2 delta)
    {
        if (mode == ViewMode.Orbit)
        {
            orbitYaw += delta.x * 0.22f;
            orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 0.18f, 8f, 70f);
        }
        else if (mode == ViewMode.Free)
        {
            transform.Rotate(-delta.y * 0.12f, delta.x * 0.12f, 0f, Space.Self);
            Vector3 euler = transform.eulerAngles;
            transform.eulerAngles = new Vector3(euler.x, euler.y, 0f);
        }
    }

    public void Zoom(float delta)
    {
        if (mode == ViewMode.Free)
            transform.position += transform.forward * delta * 0.7f;
        else
            distance = Mathf.Clamp(distance - delta * 0.7f, 4f, 18f);
    }

    void LateUpdate()
    {
        if (target == null || mode == ViewMode.Free) return;

        Vector3 lookTarget = target.position + Vector3.up * 0.3f;
        Vector3 desiredPosition;
        Quaternion desiredRotation;

        if (mode == ViewMode.Top)
        {
            desiredPosition = target.position + Vector3.up * topHeight;
            desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.forward);
        }
        else if (mode == ViewMode.Orbit)
        {
            Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            desiredPosition = lookTarget + orbit * Vector3.back * distance;
            desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);
        }
        else
        {
            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            desiredPosition = lookTarget + yaw * Vector3.back * distance + Vector3.up * height;
            desiredRotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);
        }

        float positionT = 1f - Mathf.Exp(-positionDamping * Time.deltaTime);
        float rotationT = 1f - Mathf.Exp(-rotationDamping * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
    }
}

public class DroneViewportInput : MonoBehaviour, IDragHandler, IScrollHandler
{
    public DroneViewportCameraController controller;

    public void OnDrag(PointerEventData eventData)
    {
        controller?.Drag(eventData.delta);
    }

    public void OnScroll(PointerEventData eventData)
    {
        controller?.Zoom(eventData.scrollDelta.y);
    }
}

/// Crops the render texture to cover its panel while preserving camera aspect.
public class DroneViewportCover : MonoBehaviour
{
    public RawImage image;

    void Start() => Refresh();
    void OnRectTransformDimensionsChange() => Refresh();

    void Refresh()
    {
        if (image == null || image.texture == null) return;
        Rect rect = image.rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f || image.texture.height <= 0) return;

        float viewAspect = rect.width / rect.height;
        float textureAspect = image.texture.width / (float)image.texture.height;
        if (viewAspect < textureAspect)
        {
            float width = viewAspect / textureAspect;
            image.uvRect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }
        else
        {
            float height = textureAspect / viewAspect;
            image.uvRect = new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }
    }
}
