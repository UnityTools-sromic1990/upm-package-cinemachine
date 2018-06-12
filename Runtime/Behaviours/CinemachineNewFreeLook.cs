#if true

using UnityEngine;
using Cinemachine.Utility;
using UnityEngine.Serialization;
using System;
using System.Collections.Generic;

namespace Cinemachine
{
    /// <summary>
    /// A Cinemachine Camera geared towards a 3rd person camera experience.
    /// The camera orbits around its subject with three separate camera rigs defining
    /// rings around the target. Each rig has its own radius, height offset, composer,
    /// and lens settings.
    /// Depending on the camera's position along the spline connecting these three rigs,
    /// these settings are interpolated to give the final camera position and state.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
    [ExecuteInEditMode, DisallowMultipleComponent]
    [AddComponentMenu("Cinemachine/CinemachineNewFreeLook")]
    public class CinemachineNewFreeLook : CinemachineVirtualCameraBase
    {
        /// <summary>Object for the camera children to look at (the aim target)</summary>
        [Tooltip("Object for the camera children to look at (the aim target).")]
        [NoSaveDuringPlay]
        public Transform m_LookAt = null;

        /// <summary>Object for the camera children wants to move with (the body target)</summary>
        [Tooltip("Object for the camera children wants to move with (the body target).")]
        [NoSaveDuringPlay]
        public Transform m_Follow = null;

        /// <summary> Collection of parameters that influence how this virtual camera transitions from 
        /// other virtual cameras </summary>
        public TransitionParams m_Transitions;

        /// <summary>The Vertical axis.  Value is 0..1.  Chooses how to blend the child rigs</summary>
        [Header("Axis Control")]
        [Tooltip("The Vertical axis.  Value is 0..1.  0.5 is the middle rig.  Chooses how to blend the child rigs")]
        [AxisStateProperty]
        public AxisState m_VerticalAxis = new AxisState(0, 1, false, true, 2f, 0.2f, 0.1f, "Mouse Y", false);

        /// <summary>Controls how automatic recentering of the Vertical axis is accomplished</summary>
        [Tooltip("Controls how automatic recentering of the Vertical axis is accomplished")]
        public AxisState.Recentering m_VerticalRecentering = new AxisState.Recentering(false, 1, 2);

        /// <summary>The Horizontal axis.  Value is -180...180.  This is passed on to the rigs' OrbitalTransposer component</summary>
        [Tooltip("The Horizontal axis.  Value is -180...180.  This is passed on to the rigs' OrbitalTransposer component")]
        [AxisStateProperty]
        public AxisState m_HorizontalAxis = new AxisState(-180, 180, true, false, 300f, 0.1f, 0.1f, "Mouse X", true);

        /// <summary>Controls how automatic recentering of the Horizontal axis is accomplished</summary>
        [Tooltip("Controls how automatic recentering of the Horizontal axis is accomplished")]
        public AxisState.Recentering m_HorizontalRecentering = new AxisState.Recentering(false, 1, 2);

        [Tooltip("The Radial axis.  Value is the base radius of the orbits")]
        [AxisStateProperty]
        public AxisState m_RadialAxis = new AxisState(1, 10, false, true, 2f, 0.2f, 0.1f, "Mouse ScrollWheel", false);

        /// <summary>Controls how automatic recentering of the Radial axis is accomplished</summary>
        [Tooltip("Controls how automatic recentering of the Radial axis is accomplished")]
        public AxisState.Recentering m_RadialRecentering = new AxisState.Recentering(false, 1, 2);

        /// <summary>The coordinate space to use when interpreting the offset from the target</summary>
        [Header("Orbits")]
        [Tooltip("The coordinate space to use when interpreting the offset from the target.  "
            + "This is also used to set the camera's Up vector, which will be maintained when aiming the camera.")]
        public CinemachineOrbitalTransposer.BindingMode m_BindingMode 
            = CinemachineOrbitalTransposer.BindingMode.SimpleFollowWithWorldUp;

        /// <summary>The definition of Forward.  This defines the meaning of horizontal axis value 0</summary>
        [OrbitalTransposerHeadingProperty]
        [Tooltip("The definition of Forward.  This defines the meaning of horizontal axis value 0.")]
        public CinemachineOrbitalTransposer.Heading m_Heading 
            = new CinemachineOrbitalTransposer.Heading(
                CinemachineOrbitalTransposer.Heading.HeadingDefinition.TargetForward, 4, 0);

        /// <summary></summary>
        [Tooltip("Controls how taut is the line that connects the rigs' orbits, which determines final placement on the Y axis")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("m_SplineTension")]
        public float m_SplineCurvature = 0.2f;

        [Serializable]
        // GML fixme should be internal
        public struct Rig
        {
            /// <summary>Height relative to target</summary>
            public float m_Height; 
            /// <summary>Radius of orbit</summary>
            public float m_Radius; 

            public bool m_CustomLens;

            [LensSettingsProperty]
            public LensSettings m_Lens;

            [NonSerialized]
            public List<CinemachineComponentBase> m_Components;
        }

        [SerializeField, HideInInspector]
        // GML fixme should be internal
        public Rig[] m_Rigs = new Rig[3];

        /// <summary>Updates the child rig cache</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            InvalidateRigCache();
        }

        void Reset()
        {
            //DestroyRigs(); // GML TODO
        }

        /// <summary>The cacmera state, which will be a blend of the child rig states</summary>
        override public CameraState State { get { return m_State; } }
        CameraState m_State = CameraState.Default;          // Current state this frame

        /// <summary>Get the current LookAt target.  Returns parent's LookAt if parent
        /// is non-null and no specific LookAt defined for this camera</summary>
        override public Transform LookAt
        {
            get { return ResolveLookAt(m_LookAt); }
            set { m_LookAt = value; }
        }

        /// <summary>Get the current Follow target.  Returns parent's Follow if parent
        /// is non-null and no specific Follow defined for this camera</summary>
        override public Transform Follow
        {
            get { return ResolveFollow(m_Follow); }
            set { m_Follow = value; }
        }

        /// <summary>This is called to notify the vcam that a target got warped,
        /// so that the vcam can update its internal state to make the camera 
        /// also warp seamlessy.</summary>
        /// <param name="target">The object that was warped</param>
        /// <param name="positionDelta">The amount the target's position changed</param>
        public override void OnTargetObjectWarped(Transform target, Vector3 positionDelta)
        {
            UpdateRigCache();
            for (int i = 0; i < 3; ++i)
            {
                var components = m_Rigs[i].m_Components;
                for (int j = 0; j < components.Count; ++j)
                    components[j].OnTargetObjectWarped(target, positionDelta);
            }
            base.OnTargetObjectWarped(target, positionDelta);
        }

        /// <summary>Internal use only.  Called by CinemachineCore at designated update time
        /// so the vcam can position itself and track its targets.  All 3 child rigs are updated,
        /// and a blend calculated, depending on the value of the Y axis.</summary>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than 0)</param>
        override public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime)
        {
            if (!PreviousStateIsValid)
                deltaTime = -1;

            UpdateRigCache();

            // Update the current state by invoking the component pipeline
            m_State = CalculateNewState(worldUp, deltaTime);
            ApplyPositionBlendMethod(ref m_State, m_Transitions.m_BlendHint);

            // Push the raw position back to the game object's transform, so it
            // moves along with the camera.  Leave the orientation alone, because it
            // screws up camera dragging when there is a LookAt behaviour.
            if (Follow != null)
                transform.position = State.RawPosition;

            InvokePostPipelineStageCallback(this, CinemachineCore.Stage.Finalize, ref m_State, deltaTime);
            PreviousStateIsValid = true;
        }

        /// <summary>If we are transitioning from another FreeLook, grab the axis values from it.</summary>
        /// <param name="fromCam">The camera being deactivated.  May be null.</param>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than or equal to 0)</param>
        public override void OnTransitionFromCamera(
            ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) 
        {
            base.OnTransitionFromCamera(fromCam, worldUp, deltaTime);
            if (m_Transitions.m_InheritPosition)
            {
                var brain = CinemachineCore.Instance.FindPotentialTargetBrain(this);
                if (brain != null)
                {
                    transform.position = brain.transform.position;
                    transform.rotation = brain.transform.rotation;
                    m_State = PullStateFromVirtualCamera(worldUp, ref m_Rigs[0].m_Lens);
                    m_Rigs[1].m_Lens.SnapshotCameraReadOnlyProperties(ref m_Rigs[0].m_Lens);
                    m_Rigs[2].m_Lens.SnapshotCameraReadOnlyProperties(ref m_Rigs[0].m_Lens);
                    PreviousStateIsValid = false;
                    InternalUpdateCameraState(worldUp, deltaTime);
                }
            }
            if (m_Transitions.m_OnCameraLive != null)
                m_Transitions.m_OnCameraLive.Invoke(this);
        }

        public void InvalidateRigCache()
        {
            for (int i = 0; i < 3; ++i)
                m_Rigs[i].m_Components = null;
        }

        void UpdateRigCache()
        {
            bool rebuild = false;
            for (int i = 0; !rebuild && i < 3; ++i)
            if (m_Rigs[i].m_Components == null)
                rebuild = true;
            if (rebuild)
            {
                for (int i = 0; i < 3; ++i)
                    m_Rigs[i].m_Components = new List<CinemachineComponentBase>();
                var existing = GetComponents<CinemachineComponentBase>();
                for (int stage = 0; stage < (int)CinemachineCore.Stage.Finalize; ++stage)
                {
                    int numFound = 0;
                    for (int i = 0; i < existing.Length && numFound < 3; ++i)
                    {
                        if ((int)existing[i].Stage == stage)
                        {
                            if (stage == (int)CinemachineCore.Stage.Body)
                            {
                                var a = existing[i] as CinemachineOrbitalTransposer;
                                if (a == null)
                                    continue; // Only allow orbital transposer in body, ignore others
                                a.m_HeadingIsSlave = true;
                            }
                            m_Rigs[numFound].m_Components.Add(existing[i]);
                            //GML existing[i].hideFlags |= HideFlags.HideInInspector;
                            ++numFound;
                        }
                    }
                    while (numFound < 3)
                        m_Rigs[numFound++].m_Components.Add(null);
                }
            }
        }
        
        private float GetVerticalAxisValue()
        {
            float range = m_VerticalAxis.m_MaxValue - m_VerticalAxis.m_MinValue;
            return (range > UnityVectorExtensions.Epsilon) ? m_VerticalAxis.Value / range : 0.5f;
        }

        /// <summary>
        /// Returns the local position of the camera along the spline used to connect the
        /// three camera rigs. Does not take into account the current heading of the
        /// camera (or its target)
        /// </summary>
        /// <param name="t">The t-value for the camera on its spline. Internally clamped to
        /// the value [0,1]</param>
        /// <returns>The local offset (back + up) of the camera WRT its target based on the
        /// supplied t-value</returns>
        public Vector3 GetLocalPositionForCameraFromInput(float t)
        {
            UpdateCachedSpline();
            int n = 1;
            if (t > 0.5f)
            {
                t -= 0.5f;
                n = 2;
            }
            return SplineHelpers.Bezier3(
                t * 2f, m_CachedKnots[n], m_CachedCtrl1[n], m_CachedCtrl2[n], m_CachedKnots[n+1]);
        }
                
        Vector2[] m_CachedOrbits;
        float m_CachedTension;
        Vector4[] m_CachedKnots;
        Vector4[] m_CachedCtrl1;
        Vector4[] m_CachedCtrl2;
        void UpdateCachedSpline()
        {
            bool cacheIsValid = (m_CachedOrbits != null && m_CachedTension == m_SplineCurvature);
            for (int i = 0; i < 3 && cacheIsValid; ++i)
                cacheIsValid = (m_CachedOrbits[i].y == m_Rigs[i].m_Height 
                    && m_CachedOrbits[i].x == m_Rigs[i].m_Radius);
            if (!cacheIsValid)
            {
                float t = m_SplineCurvature;
                m_CachedKnots = new Vector4[5];
                m_CachedCtrl1 = new Vector4[5];
                m_CachedCtrl2 = new Vector4[5];
                m_CachedKnots[1] = new Vector4(0, m_Rigs[2].m_Height, -m_Rigs[2].m_Radius, 0);
                m_CachedKnots[2] = new Vector4(0, m_Rigs[1].m_Height, -m_Rigs[1].m_Radius, 0);
                m_CachedKnots[3] = new Vector4(0, m_Rigs[0].m_Height, -m_Rigs[0].m_Radius, 0);
                m_CachedKnots[0] = Vector4.Lerp(m_CachedKnots[1], Vector4.zero, t);
                m_CachedKnots[4] = Vector4.Lerp(m_CachedKnots[3], Vector4.zero, t);
                SplineHelpers.ComputeSmoothControlPoints(
                    ref m_CachedKnots, ref m_CachedCtrl1, ref m_CachedCtrl2);
                m_CachedOrbits = new Vector2[3];
                for (int i = 0; i < 3; ++i)
                    m_CachedOrbits[i] = new Vector2(m_Rigs[i].m_Radius, m_Rigs[i].m_Height);
                m_CachedTension = m_SplineCurvature;
            }
        }

        private CameraState CalculateNewState(Vector3 worldUp, float deltaTime)
        {
            CameraState state = PullStateFromVirtualCamera(worldUp, ref m_Rigs[0].m_Lens);
            m_Rigs[1].m_Lens.SnapshotCameraReadOnlyProperties(ref m_Rigs[0].m_Lens);
            m_Rigs[2].m_Lens.SnapshotCameraReadOnlyProperties(ref m_Rigs[0].m_Lens);

            // Update our axes
            bool activeCam = (deltaTime >= 0) || CinemachineCore.Instance.IsLive(this);
            if (activeCam)
            {
                //if (m_HorizontalAxis.Update(deltaTime))
                //    m_HorizontalRecentering.CancelRecentering();
                if (m_VerticalAxis.Update(deltaTime))
                    m_VerticalRecentering.CancelRecentering();
                if (m_RadialAxis.Update(deltaTime))
                    m_RadialRecentering.CancelRecentering();
            }
            //m_HorizontalRecentering.DoRecentering(ref m_VerticalAxis, deltaTime, 0.5f);
            m_VerticalRecentering.DoRecentering(ref m_VerticalAxis, deltaTime, 0.5f);
            m_RadialRecentering.DoRecentering(ref m_RadialAxis, deltaTime, 0.5f);

            // Set the Reference LookAt
            Transform lookAtTarget = LookAt;
            if (lookAtTarget != null)
                state.ReferenceLookAt = lookAtTarget.position;

            // Blend from the appropriate rigs
            float y = GetVerticalAxisValue();
            Vector3 followOffset = GetLocalPositionForCameraFromInput(y);
            int otherRig = 0;
            if (y < 0.5f)
            {
                y = 1 - (y * 2);
                otherRig = 2;   // bottom
            }
            else
            {
                y = (y - 0.5f) * 2f;
                otherRig = 1;   // top
            }

            CameraState state1 = state;
            for (CinemachineCore.Stage stage = CinemachineCore.Stage.Body; 
                stage < CinemachineCore.Stage.Finalize; ++stage)
            {
                var c0 = m_Rigs[0].m_Components[(int)stage];
                if (c0 == null)
                    continue;
                c0.PrePipelineMutateCameraState(ref state);
                var c1 = m_Rigs[otherRig].m_Components[(int)stage];
                if (c1 != null)
                    c1.PrePipelineMutateCameraState(ref state1);
            }
            for (CinemachineCore.Stage stage = CinemachineCore.Stage.Body; 
                stage < CinemachineCore.Stage.Finalize; ++stage)
            {
                var c0 = m_Rigs[0].m_Components[(int)stage];
                if (c0 == null)
                {
                    if (stage == CinemachineCore.Stage.Aim)
                        state.BlendHint |= CameraState.BlendHintValue.IgnoreLookAtTarget;
                    continue;
                }
                c0.MutateCameraState(ref state, deltaTime);

                var c1 = m_Rigs[otherRig].m_Components[(int)stage];
                if (c1 != null)
                {
                    // Special case for the orbital transposer - sync them up
                    if (stage == CinemachineCore.Stage.Body)
                    {
                        var a = c0 as CinemachineOrbitalTransposer;
                        var b = c1 as CinemachineOrbitalTransposer;
                        if (a != null && b != null)
                        {
                            a.m_FollowOffset = b.m_FollowOffset = followOffset;
                            b.m_BindingMode = a.m_BindingMode = m_BindingMode;
                            b.m_Heading = a.m_Heading = m_Heading;
                            b.m_XAxis = a.m_XAxis = m_HorizontalAxis;
                            b.m_RecenterToTargetHeading.m_enabled = false;
                        }
                    }
                    c1.MutateCameraState(ref state, deltaTime);
                    switch (stage)
                    {
                        case CinemachineCore.Stage.Body: 
                            state.RawPosition = Vector3.Lerp(state.RawPosition, state1.RawPosition, y);
                            break;
                        case CinemachineCore.Stage.Aim: 
                            state.RawOrientation = Quaternion.Slerp(state.RawOrientation, state1.RawOrientation, y);
                            break;
                        case CinemachineCore.Stage.Noise: 
                            state.PositionCorrection = Vector3.Lerp(state.PositionCorrection, state1.PositionCorrection, y);
                            state.OrientationCorrection = Quaternion.Slerp(state.OrientationCorrection, state1.OrientationCorrection, y);
                            break;
                    }
                }
                InvokePostPipelineStageCallback(this, stage, ref state, deltaTime);
                state1.RawPosition = state.RawPosition;
                state1.RawOrientation = state.RawOrientation;
                state1.PositionCorrection = state.PositionCorrection;
                state1.OrientationCorrection = state.OrientationCorrection;
            }
            return state;
        }
    }
}
#endif