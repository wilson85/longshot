using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;

namespace Longshot.Gameplay.Table;

public class LaserPocketGate : Component
{
    [BindComponent(source: BindComponentSource.Children)]
    private PhotometricTubeAreaLight _light = null;

    [BindComponent(source: BindComponentSource.Children)]
    private CapsuleMesh _capsuleMesh = null;

    [BindComponent(source: BindComponentSource.Children)]
    private MaterialComponent _material = null;

    private float _length;
    public float Length
    {
        get => _length;
        set
        {
            _length = value;
            UpdateVisuals();
        }
    }

    private float _radius;
    public float Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            UpdateVisuals();
        }
    }

    private Color _gateColor = new Color(255, 0, 0, 255);
    public Color GateColor
    {
        get => _gateColor;
        set
        {
            _gateColor = value;
            UpdateVisuals();
        }
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_capsuleMesh != null)
        {
            _capsuleMesh.Height = _length;
            _capsuleMesh.Radius = _radius;
        }

        if (_light != null)
        {
            _light.Color = _gateColor;
            _light.Radius = _radius;
            _light.Length = _length;
        }

        if (_material != null && _material.Material != null)
        {
            var emissiveVec = _gateColor.ToVector3();

            float[] floatData = { emissiveVec.X, emissiveVec.Y, emissiveVec.Z };
            byte[] byteData = new byte[12];
            System.Buffer.BlockCopy(floatData, 0, byteData, 0, 12);

            _material.Material.SetParameterValue("EmissiveColor", typeof(Evergine.Mathematics.Vector3), byteData);
        }
    }
}