using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClickableMarker : MonoBehaviour
{
    private Action<Vector3> onClickMarker;

    public void Init(Action<Vector3> onClickMarker)
    {
        this.onClickMarker = onClickMarker;
    }
    private void OnMouseDown()
    {
        onClickMarker(transform.position);
        Debug.Log("OnClickMarker");

    }
}
