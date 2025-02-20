﻿using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniGLTF;
using UniGLTF.M17N;
using System.IO;
using UniGLTF.MeshUtility;

namespace VRM
{
    public class VRMExporterWizard : ExportDialogBase
    {
        public const string MENU_NAME = "Export VRM 0.x...";
        public static void OpenExportMenu()
        {
            var window = (VRMExporterWizard)GetWindow(typeof(VRMExporterWizard));
            window.titleContent = new GUIContent(MENU_NAME);
            window.Show();
        }


        enum Tabs
        {
            Meta,
            Mesh,
            BlendShape,
            ExportSettings,
        }
        Tabs _tab;


        VRMExportSettings m_settings;
        Editor m_settingsInspector;


        MeshExportValidator m_meshes;
        Editor m_meshesInspector;


        VRMMetaObject m_meta;
        VRMMetaObject Meta
        {
            get { return m_meta; }
            set
            {
                if (m_meta == value)
                {
                    return;
                }
                if (m_metaEditor != null)
                {
                    UnityEditor.Editor.DestroyImmediate(m_metaEditor);
                    m_metaEditor = null;
                }
                m_meta = value;
            }
        }
        VRMMetaObject m_tmpMeta;
        Editor m_metaEditor;


        protected override void Initialize()
        {
            m_tmpMeta = ScriptableObject.CreateInstance<VRMMetaObject>();

            m_settings = ScriptableObject.CreateInstance<VRMExportSettings>();
            m_settingsInspector = Editor.CreateEditor(m_settings);

            m_meshes = ScriptableObject.CreateInstance<MeshExportValidator>();
            m_meshesInspector = Editor.CreateEditor(m_meshes);

            State.ExportRootChanged += (root) =>
            {
                // update meta
                if (root == null)
                {
                    Meta = null;
                }
                else
                {
                    if (root.TryGetComponent<VRMMeta>(out var meta))
                    {
                        Meta = meta.Meta;
                    }
                    else
                    {
                        Meta = null;
                    }

                    // default setting
                    m_settings.PoseFreeze =
                    HumanoidValidator.HasRotationOrScale(root)
                    || m_meshes.Meshes.Any(x => x.ExportBlendShapeCount > 0 && !x.HasSkinning)
                    ;
                }
            };
        }

        protected override void Clear()
        {
            // m_settingsInspector
            UnityEditor.Editor.DestroyImmediate(m_settingsInspector);
            m_settingsInspector = null;
            // m_meshesInspector
            UnityEditor.Editor.DestroyImmediate(m_meshesInspector);
            m_meshesInspector = null;
            // m_settings
            ScriptableObject.DestroyImmediate(m_settings);
            m_settings = null;

            // m_metaEditor
            UnityEditor.Editor.DestroyImmediate(m_metaEditor);
            m_metaEditor = null;
            // Meta
            Meta = null;
            ScriptableObject.DestroyImmediate(m_tmpMeta);
            m_tmpMeta = null;
            // m_meshes
            ScriptableObject.DestroyImmediate(m_meshes);
            m_meshes = null;
        }

        protected override IEnumerable<Validator> ValidatorFactory()
        {
            // ヒエラルキー　のチェック
            yield return HierarchyValidator.Validate;
            if (!State.ExportRoot)
            {
                // Root が無い
                yield break;
            }

            // Mesh/Renderer のチェック
            m_meshes.MaterialValidator = new VRMMaterialValidator();
            yield return m_meshes.Validate;

            // Humanoid のチェック
            HumanoidValidator.MeshInformations = m_meshes.Meshes;
            HumanoidValidator.EnableFreeze = m_settings.PoseFreeze;
            yield return HumanoidValidator.Validate_Normalize;
            yield return HumanoidValidator.Validate_TPose;

            //
            // VRM のチェック
            //
            VRMExporterValidator.ReduceBlendshape = m_settings.ReduceBlendshape;
            yield return VRMExporterValidator.Validate;

            yield return VRMSpringBoneValidator.Validate;

            if (State.ExportRoot.TryGetComponent<VRMFirstPerson>(out var firstPerson))
            {
                yield return firstPerson.Validate;
            }

            if (State.ExportRoot.TryGetComponent<VRMBlendShapeProxy>(out var proxy))
            {
                yield return proxy.Validate;
            }

            var meta = Meta ? Meta : m_tmpMeta;
            yield return meta.Validate;
        }

        protected override void OnLayout()
        {
            m_meshes.SetRoot(State.ExportRoot, m_settings.GltfExportSettings, new VRMBlendShapeExportFilter(State.ExportRoot, m_settings));
        }

        static bool s_foldT = true;

        protected override bool DoGUI(bool isValid)
        {
            if (State.ExportRoot == null)
            {
                return false;
            }

            //
            // T-Pose
            //
            if (State.ExportRoot.TryGetComponent<Animator>(out var animator))
            {
                var backup = GUI.enabled;
                GUI.enabled = State.ExportRoot.scene.IsValid();

                if (s_foldT = EditorGUILayout.Foldout(s_foldT, "T-Pose"))
                {
                    if (GUI.enabled)
                    {
                        EditorGUILayout.HelpBox(EnableTPose.ENALBE_TPOSE_BUTTON.Msg(), MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(EnableTPose.DISABLE_TPOSE_BUTTON.Msg(), MessageType.Warning);
                    }

                    //
                    // T-Pose
                    //
                    if (GUILayout.Button(VRMExportOptions.DO_TPOSE.Msg()))
                    {
                        if (State.ExportRoot != null)
                        {
                            // fallback
                            Undo.RecordObjects(State.ExportRoot.GetComponentsInChildren<Transform>(), "tpose");
                            VRMBoneNormalizer.EnforceTPose(State.ExportRoot);
                            Repaint();
                        }
                    }

                    if (GUILayout.Button(VRMExportOptions.DO_TPOSE.Msg() + "(unity internal)"))
                    {
                        if (State.ExportRoot != null)
                        {
                            Undo.RecordObjects(State.ExportRoot.GetComponentsInChildren<Transform>(), "tpose.internal");
                            if (InternalTPose.TryMakePoseValid(State.ExportRoot))
                            {
                                // done
                                Repaint();
                            }
                            else
                            {
                                UniGLTFLogger.Warning("not found");
                            }
                        }
                    }
                }

                GUI.enabled = backup;
            }

            if (!isValid)
            {
                return false;
            }

            //
            // GUI
            //
            _tab = TabBar.OnGUI(_tab);
            foreach (var meshInfo in m_meshes.Meshes)
            {
                switch (meshInfo.VertexColor)
                {
                    case UniGLTF.VertexColorState.ExistsAndMixed:
                        Validation.Warning($"{meshInfo.Renderers}: Both vcolor.multiply and not multiply unlit materials exist").DrawGUI();
                        break;
                }
            }
            return DrawWizardGUI();
        }

        protected override string SaveTitle => "Save vrm0";
        protected override string SaveName => $"{State.ExportRoot.name}.vrm";
        protected override string[] SaveExtensions => new string[] { "vrm" };

        protected override void ExportPath(string path)
        {
            var bytes = VRMEditorExporter.Export(State.ExportRoot, Meta != null ? Meta : m_tmpMeta, m_settings);

            File.WriteAllBytes(path, bytes);

            if (path.StartsWithUnityAssetPath())
            {
                // 出力ファイルのインポートを発動
                AssetDatabase.ImportAsset(path.ToUnityRelativePath());
            }
        }

        bool DrawWizardGUI()
        {
            if (m_tmpMeta == null)
            {
                // disabled
                return false;
            }

            // tabbar
            switch (_tab)
            {
                case Tabs.Meta:
                    if (m_metaEditor == null)
                    {
                        if (m_meta != null)
                        {
                            m_metaEditor = Editor.CreateEditor(Meta);
                        }
                        else
                        {
                            m_metaEditor = Editor.CreateEditor(m_tmpMeta);
                        }
                    }
                    m_metaEditor.OnInspectorGUI();
                    break;

                case Tabs.Mesh:
                    m_meshesInspector.OnInspectorGUI();
                    break;

                case Tabs.BlendShape:
                    if (State.ExportRoot)
                    {
                        OnBlendShapeGUI(State.ExportRoot.GetComponentOrNull<VRMBlendShapeProxy>());
                    }
                    break;

                case Tabs.ExportSettings:
                    m_settings.Root = State.ExportRoot;
                    m_settingsInspector.OnInspectorGUI();
                    break;
            }

            return true;
        }

        enum BlendShapeTabMessages
        {
            [LangMsg(Languages.ja, "prefab は操作できません")]
            [LangMsg(Languages.en, "cannot manipulate prefab")]
            CANNOT_MANIPULATE_PREFAB,

            [LangMsg(Languages.ja, "シーン上のExportRootにBlendShapeを適用します。Exportすると適用された状態がBakeされます。")]
            [LangMsg(Languages.en, "Apply blendshpae to ExportRoot in scene. Bake scene status if Export.")]
            SCENE_MESSAGE,

            [LangMsg(Languages.ja, "選択された BlendShapeClip を適用する")]
            [LangMsg(Languages.en, "Apply selected BlendShapeClip")]
            APPLY_BLENDSHAPECLIP_BUTTON,

            [LangMsg(Languages.ja, "BlendShape を Clear する")]
            [LangMsg(Languages.en, "Clear BlendShape")]
            CLEAR_BLENDSHAPE_BUTTON,
        }

        BlendShapeMerger m_merger;

        int m_selected = 0;
        void OnBlendShapeGUI(VRMBlendShapeProxy proxy)
        {
            if (!State.ExportRoot.scene.IsValid())
            {
                EditorGUILayout.HelpBox(BlendShapeTabMessages.CANNOT_MANIPULATE_PREFAB.Msg(), MessageType.Warning);
                return;
            }

            if (!proxy)
            {
                EditorGUILayout.HelpBox("no BlendShapeProxy", MessageType.Warning);
                return;
            }
            var avatar = proxy.BlendShapeAvatar;
            if (!avatar)
            {
                return;
            }

            m_merger = new BlendShapeMerger(avatar.Clips.Where(x => x != null), proxy.transform);


            GUILayout.Space(20);

            EditorGUILayout.HelpBox(BlendShapeTabMessages.SCENE_MESSAGE.Msg(), MessageType.Info);

            var options = avatar.Clips.Where(x => x != null).Select(x => x.ToString()).ToArray();
            m_selected = EditorGUILayout.Popup("select blendshape", m_selected, options);

            if (GUILayout.Button(BlendShapeTabMessages.APPLY_BLENDSHAPECLIP_BUTTON.Msg()))
            {
                m_merger.SetValues(avatar.Clips.Where(x => x != null).Select((x, i) => new KeyValuePair<BlendShapeKey, float>(x.Key, i == m_selected ? 1 : 0)));
                m_merger.Apply();
                m_settings.PoseFreeze = true;
            }

            if (GUILayout.Button(BlendShapeTabMessages.CLEAR_BLENDSHAPE_BUTTON.Msg()))
            {
                m_merger.SetValues(avatar.Clips.Where(x => x != null).Select(x => new KeyValuePair<BlendShapeKey, float>(x.Key, 0)));
                m_merger.Apply();
            }
        }
    }
}
