using Unity.Cinemachine;
using UnityEngine;

public class MapTransition : MonoBehaviour
{
    [SerializeField] PolygonCollider2D mapBoundary;
    CinemachineConfiner2D confiner;
    [SerializeField] Direction direction;
    [SerializeField] Transform teleportTargetTransition;
    [SerializeField] float additivePos = 0;

    enum Direction { Teleport, Up, Down, Left, Right}

    private void Awake()
    {
        confiner = FindAnyObjectByType<CinemachineConfiner2D>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            FadeTransition(collision.gameObject);

            MapController_Manual.Instance?.HighlightArea(mapBoundary.name);
            MapController_Dynamic.Instance?.UpdateCurrentArea(mapBoundary.name);
        }
    }

    async void FadeTransition(GameObject player)
    {
        PauseController.SetPause(true);

        await ScreenFader.Instance.FadeOut();

        confiner.BoundingShape2D = mapBoundary;
        UpdatePlayerPosition(player);

        await ScreenFader.Instance.FadeIn();

        PauseController.SetPause(false);
    }

    private void UpdatePlayerPosition(GameObject player)
    {
        if(direction == Direction.Teleport)
        {
            player.transform.position = teleportTargetTransition.position;

            return;
        }

        Vector3 newPos = player.transform.position;

        switch (direction)
        {
            case Direction.Up:
                newPos.y += additivePos;
                break;
            case Direction.Down:
                newPos.y -= additivePos;
                break;
            case Direction.Left:
                newPos.x -= additivePos;
                break;
            case Direction.Right:
                newPos.x += additivePos;
                break;
        }

        player.transform.position = newPos;
    }
}
