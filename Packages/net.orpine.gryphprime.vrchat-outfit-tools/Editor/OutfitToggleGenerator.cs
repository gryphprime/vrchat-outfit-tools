using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace OutfitToggleGenerator
{
    internal static class OutfitToggleGenerator
    {
        private const string LegacyMenuName = "Generated Outfit Toggles";
        private const string MarkerName = "__OutfitToggleGenerator";
        private const string GeneratedParameterPrefix = "__OutfitToggle/";
        private const string IconFolder = "Assets/Generated/OutfitToggleIcons";
        private const int IconSize = 256;
        private static readonly string[] KnownBoothBaseNames =
        {
            "Kaguya", "Manuka", "Shinano", "Miltina", "Selestia", "Moe", "Chiffon", "Airi", "Kikyo", "Shinra", "Sio",
            "Mame Friends", "Milphy", "Eku", "Lumina", "Maya", "Karin", "Lapwing", "Lashu", "Ichigo", "Mafuyu",
        };

        private sealed class GeneratedToggleEntry
        {
            public GameObject gameObject;
            public ModularAvatarObjectToggle objectToggle;
            public ModularAvatarMenuItem menuItem;
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Regenerate From Selection")]
        private static void RegenerateFromSelection()
        {
            if (TryGetSelectedMenu(out var selectedMenuAvatar, out var selectedMenu))
            {
                RefreshMenuIcons(selectedMenuAvatar, selectedMenu, MenuToggleEntries(selectedMenu, generatedOnly: false));
                return;
            }
            if (!TryGetOutfitTargets(out var avatar, out var outfitRoots, out var menuHost, out var targets)) return;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Regenerate outfit toggles");
            var autoClean = FindGeneratedMenuForOutfits(outfitRoots, menuHost, avatar.transform) == null &&
                            AppleIntelligenceNameCleaner.IsAvailable();
            Transform menuRoot = null;
            try
            {
                menuRoot = FindOrCreateMenuRoot(outfitRoots, menuHost, avatar.transform);
                var iconPaths = GeneratedIconPaths(menuRoot);
                var labels = GeneratedLabels(menuRoot);
                var icons = RenderIcons(avatar.gameObject, targets);
                ClearGeneratedToggles(menuRoot);
                var created = 0;

                foreach (var target in targets)
                {
                    var toggle = new GameObject(target.name);
                    Undo.RegisterCreatedObjectUndo(toggle, "Create outfit toggle");
                    Undo.SetTransformParent(toggle.transform, menuRoot.transform, "Create outfit toggle");

                    var objectToggle = Undo.AddComponent<ModularAvatarObjectToggle>(toggle);
                    objectToggle.Objects.Add(new ToggledObject
                    {
                        Object = new AvatarObjectReference(target),
                        Active = !target.activeSelf,
                    });

                    var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(toggle);
                    menuItem.Control = new VRCExpressionsMenu.Control
                    {
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        name = target.name,
                        value = 1,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = $"{GeneratedParameterPrefix}{target.GetInstanceID()}" },
                        icon = SaveIcon(
                            icons.TryGetValue(target, out var icon) ? icon : null,
                            target,
                        iconPaths.TryGetValue(new AvatarObjectReference(target).referencePath, out var iconPath) ? iconPath : null),
                    };
                    if (labels.TryGetValue(new AvatarObjectReference(target).referencePath, out var label))
                    {
                        toggle.name = label;
                        menuItem.label = label;
                    }

                    created++;
                }

                Selection.activeGameObject = menuRoot.gameObject;
                EditorGUIUtility.PingObject(menuRoot.gameObject);
                Debug.Log($"Regenerated {created} outfit toggle(s) under '{menuRoot.name}'.", menuRoot.gameObject);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            if (autoClean)
                StartNameCleanup(menuRoot, avatar.gameObject.name, outfitRoots, targets, showErrors: false);
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Regenerate From Selection", true)]
        private static bool CanRegenerateFromSelection()
        {
            return CanUseSelection();
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Refresh Icons From Selection")]
        private static void RefreshIconsFromSelection()
        {
            if (TryGetSelectedMenu(out var selectedMenuAvatar, out var selectedMenu))
            {
                RefreshMenuIcons(selectedMenuAvatar, selectedMenu, MenuToggleEntries(selectedMenu, generatedOnly: false));
                return;
            }
            if (!TryGetOutfitTargets(out var avatar, out var outfitRoots, out var menuHost, out var targets)) return;

            var menuRoot = FindGeneratedMenuForOutfits(outfitRoots, menuHost, avatar.transform);
            if (menuRoot == null)
            {
                EditorUtility.DisplayDialog("Refresh outfit icons", "No generated menu was found for this selection.", "OK");
                return;
            }

            NormalizeMenuRoot(menuRoot, outfitRoots, menuHost);

            var selectedPaths = new HashSet<string>(targets.Select(target => new AvatarObjectReference(target).referencePath));
            var menuItems = GeneratedToggleEntries(menuRoot)
                .Where(entry => entry.objectToggle.Objects.Any(reference => selectedPaths.Contains(reference.Object?.referencePath)))
                .ToList();
            if (menuItems.Count == 0)
            {
                EditorUtility.DisplayDialog("Refresh outfit icons", "None of the selected objects has a generated toggle.", "OK");
                return;
            }

            RefreshMenuIcons(avatar, menuRoot, menuItems);
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Refresh Icons From Selection", true)]
        private static bool CanRefreshIconsFromSelection()
        {
            return CanUseSelection();
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Clean Names With Apple Intelligence")]
        private static void CleanNamesWithAppleIntelligence()
        {
            if (TryGetSelectedMenu(out var selectedMenuAvatar, out var selectedMenu))
            {
                CleanMenuNames(selectedMenuAvatar, selectedMenu);
                return;
            }
            if (!TryGetOutfitTargets(out var avatar, out var outfitRoots, out var menuHost, out var targets)) return;
            if (!AppleIntelligenceNameCleaner.IsAvailable())
            {
                EditorUtility.DisplayDialog(
                    "Apple Intelligence unavailable",
                    "Build the macOS bridge, then enable Apple Intelligence on a supported macOS 26+ Apple Silicon Mac.",
                    "OK");
                return;
            }

            var menuRoot = FindGeneratedMenuForOutfits(outfitRoots, menuHost, avatar.transform);
            if (menuRoot == null)
            {
                EditorUtility.DisplayDialog("Clean toggle names", "Regenerate this selection before cleaning its toggle names.", "OK");
                return;
            }

            StartNameCleanup(menuRoot, avatar.gameObject.name, outfitRoots, targets, showErrors: true);
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Clean Names With Apple Intelligence", true)]
        private static bool CanCleanNamesWithAppleIntelligence()
        {
            return CanUseSelection() && AppleIntelligenceNameCleaner.IsAvailable();
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Cluster Generated Toggles With Apple Intelligence")]
        private static void ClusterGeneratedToggles()
        {
            if (TryGetSelectedMenu(out var selectedMenuAvatar, out var selectedMenu))
            {
                ClusterMenu(selectedMenuAvatar, selectedMenu);
                return;
            }
            if (!TryGetOutfitTargets(out var avatar, out var outfitRoots, out var menuHost, out _)) return;
            var menuRoot = FindGeneratedMenuForOutfits(outfitRoots, menuHost, avatar.transform);
            if (menuRoot == null)
            {
                EditorUtility.DisplayDialog("Cluster toggles", "Regenerate this selection before clustering its toggles.", "OK");
                return;
            }

            ClusterMenu(
                avatar,
                menuRoot,
                string.Join(", ", outfitRoots.Select(OutfitMenuName)),
                string.Join("\n", outfitRoots.Select(OutfitPrefabPath)));
        }

        private static void ClusterMenu(
            VRCAvatarDescriptor avatar,
            Transform menuRoot,
            string outfitNames = null,
            string outfitPaths = null)
        {
            if (!AppleIntelligenceNameCleaner.IsAvailable())
            {
                EditorUtility.DisplayDialog("Apple Intelligence unavailable", "Enable Apple Intelligence before clustering toggles.", "OK");
                return;
            }

            var toggles = GeneratedToggleEntries(menuRoot);
            if (toggles.Count < 2)
            {
                EditorUtility.DisplayDialog("Cluster toggles", "This menu needs at least two generated toggles. Manual controls are not changed.", "OK");
                return;
            }

            var candidates = toggles.Select((toggle, index) => new ToggleClusterCandidate
            {
                id = index + 1,
                label = toggle.menuItem.label,
                path = string.Join("\n", toggle.objectToggle.Objects.Select(entry => entry.Object?.referencePath).Where(path => !string.IsNullOrEmpty(path))),
            }).ToArray();
            AppleIntelligenceToggleClusterer.Cluster(
                avatar.gameObject.name,
                outfitNames ?? menuRoot.name,
                outfitPaths ?? AnimationUtility.CalculateTransformPath(menuRoot, avatar.transform),
                candidates,
                groups => ApplyClusters(menuRoot, toggles, groups),
                error => EditorUtility.DisplayDialog("Apple Intelligence clustering failed", error, "OK"));
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Cluster Generated Toggles With Apple Intelligence", true)]
        private static bool CanClusterGeneratedToggles()
        {
            return CanUseSelection() && AppleIntelligenceNameCleaner.IsAvailable();
        }

        [MenuItem("Tools/Avatar Outfit Toggles/Run Self Check")]
        private static void RunSelfCheck()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            try
            {
                Debug.Assert(TopLevelTargets(new[] { parent, child }).SequenceEqual(new[] { parent }),
                    "Nested selections must produce one toggle.");
                var sibling = new GameObject("Sibling");
                sibling.transform.SetParent(parent.transform);
                Debug.Assert(CommonMenuHost(new[] { child.transform, sibling.transform }) == parent.transform,
                    "Multiple outfits must use their nearest common parent as the menu host.");
                parent.AddComponent<MeshRenderer>();
                child.AddComponent<MeshRenderer>();
                Debug.Assert(ToggleTargets(new[] { parent }).SequenceEqual(new[] { child }),
                    "An outfit root must create toggles for its renderable children.");
                Debug.Assert(Vector3.Dot(Quaternion.Euler(10f, 180f, 0f) * Vector3.forward, Vector3.back) > 0.9f,
                    "The preview camera must face the avatar's front.");
                var previewCamera = new GameObject("Preview Camera").AddComponent<Camera>();
                previewCamera.transform.SetParent(parent.transform);
                previewCamera.fieldOfView = 90f;
                previewCamera.aspect = 1f;
                Debug.Assert(Mathf.Approximately(CameraDistance(previewCamera, new Bounds(Vector3.zero, Vector3.one * 2f)), 2f),
                    "Icon framing must fit the nearest mesh bounds.");
                Debug.Assert(OutfitTitle("Assets/MONVIE/SecretServant/Prefeb/Shinano/Color 1.prefab", "Color 1") == "Secret Servant",
                    "Generic color prefabs must use their outfit folder for the menu title.");
                Debug.Assert(CleanGeneratedLabel("Secret Servant BackRibbon", "Secret Servant", "SecretServant_BackRibbon") == "Back Ribbon",
                    "Toggle labels must not repeat their generated menu title.");
                Debug.Assert(CleanGeneratedLabel("Secret Servant Leg Accent", "Secret Servant", "SecretServant_LegAcc") == "Leg Accessory",
                    "Accessory abbreviations must remain accessories after cleanup.");
                Debug.Assert(CleanGeneratedLabel("C_Knee Socks_(USE_Toe_heels)", "For Shinano", "C_Knee Socks_(USE_Toe_heels)") == "Knee Socks (USE Toe heels)",
                    "Local cleanup must remove technical category prefixes.");
                Debug.Assert(HasConflictingLocations(new[] { "Chain Ankle", "Chain Waist" }),
                    "Location-specific chain toggles must stay separate.");
                Debug.Assert(HasConflictingLocations(new[] { "Bikini Bottom", "Bikini Tops" }),
                    "Bikini tops and bottoms must stay separate.");
                Debug.Assert(!HasConflictingLocations(new[] { "Bag", "Bag Charm", "Bag Hand Strap" }),
                    "Dependent bag parts must remain eligible for one toggle.");
                var alphaBounds = ContentBounds(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 255) }, 2, 2);
                Debug.Assert(alphaBounds.x == 1 && alphaBounds.y == 1 && alphaBounds.width == 1 && alphaBounds.height == 1,
                    "Icon fitting must use only visible pixels.");
                var generated = new GameObject("Generated");
                generated.transform.SetParent(parent.transform);
                var generatedMenu = generated.AddComponent<ModularAvatarMenuItem>();
                generatedMenu.Control = new VRCExpressionsMenu.Control
                {
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = GeneratedParameterPrefix + "test" },
                };
                Debug.Assert(IsGeneratedToggle(generated.AddComponent<ModularAvatarObjectToggle>()),
                    "Generated toggles must retain their parameter marker.");
                var manualMenu = new GameObject("Manual Menu");
                manualMenu.transform.SetParent(parent.transform);
                manualMenu.AddComponent<ModularAvatarMenuItem>().MenuSource = SubmenuSource.Children;
                var manualToggle = new GameObject("Manual Toggle");
                manualToggle.transform.SetParent(manualMenu.transform);
                manualToggle.AddComponent<ModularAvatarMenuItem>().Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.Toggle };
                manualToggle.AddComponent<ModularAvatarObjectToggle>();
                Debug.Assert(MenuToggleEntries(manualMenu.transform, generatedOnly: false).Count == 1 &&
                             MenuToggleEntries(manualMenu.transform, generatedOnly: true).Count == 0,
                    "Manual menu icons must be discoverable without making manual toggles clusterable.");
                Debug.Log("Outfit Toggle Generator self-check passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        internal static List<GameObject> TopLevelTargets(IEnumerable<GameObject> selection)
        {
            var targets = selection.Where(target => target != null).Distinct().ToList();
            return targets.Where(target => !targets.Any(other => other != target && target.transform.IsChildOf(other.transform))).ToList();
        }

        internal static List<GameObject> ToggleTargets(IEnumerable<GameObject> selection)
        {
            var roots = TopLevelTargets(selection);
            var renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Select(renderer => renderer.gameObject)
                .Distinct()
                .ToList();

            return renderers.Count == 0
                ? roots
                : renderers.Where(target => !renderers.Any(other => other != target && other.transform.IsChildOf(target.transform))).ToList();
        }

        private static bool CanUseSelection()
        {
            return Selection.gameObjects.Any(target => target.GetComponentInParent<VRCAvatarDescriptor>() != null);
        }

        private static bool TryGetSelectedMenu(out VRCAvatarDescriptor avatar, out Transform menuRoot)
        {
            avatar = null;
            menuRoot = null;
            var selection = TopLevelTargets(Selection.gameObjects);
            if (selection.Count != 1) return false;

            var menuItem = selection[0].GetComponent<ModularAvatarMenuItem>();
            avatar = selection[0].GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null || menuItem == null || menuItem.MenuSource != SubmenuSource.Children) return false;
            menuRoot = selection[0].transform;
            return true;
        }

        private static bool TryGetOutfitTargets(
            out VRCAvatarDescriptor avatar,
            out List<Transform> outfitRoots,
            out Transform menuHost,
            out List<GameObject> targets)
        {
            targets = TopLevelTargets(Selection.gameObjects);
            avatar = null;
            outfitRoots = null;
            menuHost = null;
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Outfit toggles", "Select one or more outfit objects under the same avatar.", "OK");
                return false;
            }

            avatar = targets[0].GetComponentInParent<VRCAvatarDescriptor>();
            var selectedAvatar = avatar;
            if (selectedAvatar == null || targets.Any(target => target.GetComponentInParent<VRCAvatarDescriptor>() != selectedAvatar))
            {
                EditorUtility.DisplayDialog("Outfit toggles", "Every selected object must belong to the same VRChat avatar.", "OK");
                return false;
            }

            targets.RemoveAll(target => target == selectedAvatar.gameObject);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Outfit toggles", "Select one or more outfit roots below the avatar root.", "OK");
                return false;
            }

            outfitRoots = targets.Select(target => target.transform).ToList();
            menuHost = CommonMenuHost(outfitRoots);
            if (menuHost == null)
            {
                EditorUtility.DisplayDialog("Outfit toggles", "The selected outfits need a common parent.", "OK");
                return false;
            }

            targets = ToggleTargets(targets);
            if (targets.Count > 0) return true;

            EditorUtility.DisplayDialog("Outfit toggles", "Select outfit objects below the avatar root, not the avatar itself.", "OK");
            return false;
        }

        private static Transform CommonMenuHost(IReadOnlyList<Transform> outfitRoots)
        {
            if (outfitRoots.Count == 1) return outfitRoots[0];
            for (var candidate = outfitRoots[0].parent; candidate != null; candidate = candidate.parent)
                if (outfitRoots.All(root => root.IsChildOf(candidate))) return candidate;
            return null;
        }

        private static Transform FindOrCreateMenuRoot(
            IReadOnlyList<Transform> outfitRoots,
            Transform menuHost,
            Transform avatarRoot)
        {
            var existing = FindGeneratedMenuForOutfits(outfitRoots, menuHost, avatarRoot);
            if (existing != null)
            {
                NormalizeMenuRoot(existing, outfitRoots, menuHost);
                return existing;
            }

            var menuName = GeneratedMenuName(outfitRoots);
            var menuRoot = new GameObject(menuName);
            Undo.RegisterCreatedObjectUndo(menuRoot, "Create outfit toggle menu");
            Undo.SetTransformParent(menuRoot.transform, menuHost, "Create outfit toggle menu");

            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(menuRoot);
            menuItem.MenuSource = SubmenuSource.Children;
            menuItem.Control = new VRCExpressionsMenu.Control
            {
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                name = menuName,
            };
            Undo.AddComponent<ModularAvatarMenuInstaller>(menuRoot);
            EnsureMarker(menuRoot.transform, outfitRoots);
            return menuRoot.transform;
        }

        private static Transform FindGeneratedMenuForOutfits(
            IReadOnlyList<Transform> outfitRoots,
            Transform menuHost,
            Transform avatarRoot)
        {
            foreach (Transform child in menuHost)
                if (IsGeneratedMenuRoot(child) && IsMenuForOutfits(child, outfitRoots, avatarRoot)) return child;

            if (menuHost != avatarRoot)
                foreach (Transform child in avatarRoot)
                    if (IsGeneratedMenuRoot(child) && IsMenuForOutfits(child, outfitRoots, avatarRoot)) return child;

            return null;
        }

        private static void NormalizeMenuRoot(
            Transform menuRoot,
            IReadOnlyList<Transform> outfitRoots,
            Transform menuHost)
        {
            if (menuRoot.parent != menuHost)
                Undo.SetTransformParent(menuRoot, menuHost, "Move outfit toggle menu");

            if (menuRoot.name == LegacyMenuName || outfitRoots.Count == 1 && menuRoot.name == outfitRoots[0].name)
            {
                var menuName = GeneratedMenuName(outfitRoots);
                Undo.RecordObject(menuRoot.gameObject, "Rename outfit toggle menu");
                menuRoot.name = menuName;
                var menuItem = menuRoot.GetComponent<ModularAvatarMenuItem>();
                if (menuItem != null)
                {
                    Undo.RecordObject(menuItem, "Rename outfit toggle menu");
                    menuItem.label = menuName;
                    if (menuItem.Control != null) menuItem.Control.name = menuName;
                    EditorUtility.SetDirty(menuItem);
                }
            }

            EnsureMarker(menuRoot, outfitRoots);
        }

        private static bool IsGeneratedMenuRoot(Transform root)
        {
            var menuItem = root.GetComponent<ModularAvatarMenuItem>();
            if (menuItem?.Control?.type != VRCExpressionsMenu.Control.ControlType.SubMenu ||
                menuItem.MenuSource != SubmenuSource.Children ||
                root.GetComponent<ModularAvatarMenuInstaller>() == null) return false;

            return root.Find(MarkerName) != null ||
                   root.name == LegacyMenuName && root.GetComponentsInChildren<ModularAvatarObjectToggle>(true).Any(IsGeneratedToggle);
        }

        private static bool IsMenuForOutfits(
            Transform menuRoot,
            IReadOnlyList<Transform> outfitRoots,
            Transform avatarRoot)
        {
            var outfitPaths = OutfitPaths(outfitRoots, avatarRoot);
            var markerTransform = menuRoot.Find(MarkerName);
            var marker = markerTransform?.GetComponent<OutfitToggleGeneratedMenu>();
            if (marker?.outfitPaths?.Length > 0)
                return marker.outfitPaths.OrderBy(path => path).SequenceEqual(outfitPaths);

            if (markerTransform != null && !menuRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true).Any(IsGeneratedToggle))
                return true;

            return outfitPaths.All(outfitPath => menuRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true)
                .Where(IsGeneratedToggle)
                .SelectMany(toggle => toggle.Objects)
                .Any(entry => entry.Object?.referencePath == outfitPath || entry.Object?.referencePath?.StartsWith(outfitPath + "/", StringComparison.Ordinal) == true));
        }

        private static string[] OutfitPaths(IEnumerable<Transform> outfitRoots, Transform avatarRoot)
        {
            return outfitRoots
                .Select(root => AnimationUtility.CalculateTransformPath(root, avatarRoot))
                .OrderBy(path => path)
                .ToArray();
        }

        private static string GeneratedMenuName(IReadOnlyList<Transform> outfitRoots)
        {
            return outfitRoots.Count == 1 ? OutfitMenuName(outfitRoots[0]) : "Outfits";
        }

        private static void EnsureMarker(Transform menuRoot, IReadOnlyList<Transform> outfitRoots)
        {
            var marker = menuRoot.Find(MarkerName);
            if (marker == null)
            {
                var markerObject = new GameObject(MarkerName) { hideFlags = HideFlags.HideInHierarchy };
                Undo.RegisterCreatedObjectUndo(markerObject, "Tag outfit toggle menu");
                Undo.SetTransformParent(markerObject.transform, menuRoot, "Tag outfit toggle menu");
                marker = markerObject.transform;
            }

            var generatedMenu = marker.GetComponent<OutfitToggleGeneratedMenu>();
            if (generatedMenu == null) generatedMenu = Undo.AddComponent<OutfitToggleGeneratedMenu>(marker.gameObject);
            Undo.RecordObject(generatedMenu, "Tag outfit toggle menu");
            generatedMenu.outfitPaths = OutfitPaths(outfitRoots, menuRoot.GetComponentInParent<VRCAvatarDescriptor>().transform);
            EditorUtility.SetDirty(generatedMenu);
        }

        private static void ClearGeneratedToggles(Transform menuRoot)
        {
            var generated = new List<GameObject>();
            foreach (Transform child in menuRoot)
            {
                var toggle = child.GetComponent<ModularAvatarObjectToggle>();
                if (toggle != null && IsGeneratedToggle(toggle))
                    generated.Add(child.gameObject);
            }

            foreach (var toggle in generated)
                Undo.DestroyObjectImmediate(toggle);
        }

        private static Dictionary<string, string> GeneratedIconPaths(Transform menuRoot)
        {
            var paths = new Dictionary<string, string>();
            foreach (var toggle in menuRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true))
            {
                if (!IsGeneratedToggle(toggle)) continue;

                var menuItem = toggle.GetComponent<ModularAvatarMenuItem>();
                var iconPath = menuItem?.Control?.icon == null ? null : AssetDatabase.GetAssetPath(menuItem.Control.icon);
                if (string.IsNullOrEmpty(iconPath)) continue;

                foreach (var entry in toggle.Objects)
                    if (entry.Object != null && !string.IsNullOrEmpty(entry.Object.referencePath))
                        paths[entry.Object.referencePath] = iconPath;
            }

            return paths;
        }

        private static Dictionary<string, string> GeneratedLabels(Transform menuRoot)
        {
            var labels = new Dictionary<string, string>();
            foreach (var toggle in menuRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true))
            {
                if (!IsGeneratedToggle(toggle)) continue;

                var label = toggle.GetComponent<ModularAvatarMenuItem>()?.label;
                if (string.IsNullOrWhiteSpace(label)) continue;

                foreach (var entry in toggle.Objects)
                    if (entry.Object != null && !string.IsNullOrEmpty(entry.Object.referencePath))
                        labels[entry.Object.referencePath] = label;
            }

            return labels;
        }

        private static bool IsGeneratedToggle(ModularAvatarObjectToggle toggle)
        {
            var parameter = toggle.GetComponent<ModularAvatarMenuItem>()?.Control?.parameter?.name;
            return !string.IsNullOrEmpty(parameter) && parameter.StartsWith(GeneratedParameterPrefix, StringComparison.Ordinal);
        }

        private static List<GeneratedToggleEntry> GeneratedToggleEntries(Transform menuRoot)
        {
            return MenuToggleEntries(menuRoot, generatedOnly: true);
        }

        private static List<GeneratedToggleEntry> MenuToggleEntries(Transform menuRoot, bool generatedOnly)
        {
            var toggles = new List<GeneratedToggleEntry>();
            foreach (Transform child in menuRoot)
            {
                var objectToggle = child.GetComponent<ModularAvatarObjectToggle>();
                var menuItem = child.GetComponent<ModularAvatarMenuItem>();
                if (objectToggle == null || menuItem == null || generatedOnly && !IsGeneratedToggle(objectToggle)) continue;
                toggles.Add(new GeneratedToggleEntry
                {
                    gameObject = child.gameObject,
                    objectToggle = objectToggle,
                    menuItem = menuItem,
                });
            }

            return toggles;
        }

        private static List<GameObject> ReferencedObjects(Transform avatarRoot, ModularAvatarObjectToggle objectToggle)
        {
            var objects = new List<GameObject>();
            foreach (var entry in objectToggle.Objects)
            {
                var path = entry.Object?.referencePath;
                var target = string.IsNullOrEmpty(path) ? avatarRoot : avatarRoot.Find(path);
                if (target != null) objects.Add(target.gameObject);
            }

            return objects.Distinct().ToList();
        }

        private static void RefreshMenuIcons(
            VRCAvatarDescriptor avatar,
            Transform menuRoot,
            IEnumerable<GeneratedToggleEntry> entries)
        {
            var menuItems = entries.ToList();
            if (menuItems.Count == 0)
            {
                EditorUtility.DisplayDialog("Refresh outfit icons", "This menu has no child MA Object Toggles.", "OK");
                return;
            }

            var iconGroups = menuItems.Select(entry => new KeyValuePair<GeneratedToggleEntry, IEnumerable<GameObject>>(
                    entry,
                    ReferencedObjects(avatar.transform, entry.objectToggle)))
                .Where(entry => entry.Value.Any())
                .ToList();
            var icons = RenderIconGroups(avatar.gameObject, iconGroups);
            var refreshed = 0;
            foreach (var entry in menuItems)
            {
                if (!icons.TryGetValue(entry, out var icon)) continue;
                var currentPath = entry.menuItem.Control.icon == null ? null : AssetDatabase.GetAssetPath(entry.menuItem.Control.icon);
                var refreshedIcon = SaveIcon(icon, entry.gameObject, currentPath);
                if (refreshedIcon == null) continue;

                Undo.RecordObject(entry.menuItem, "Refresh outfit toggle icon");
                entry.menuItem.Control.icon = refreshedIcon;
                EditorUtility.SetDirty(entry.menuItem);
                refreshed++;
            }

            Debug.Log($"Refreshed {refreshed} outfit toggle icon(s).", menuRoot.gameObject);
        }

        private static void ApplyClusters(
            Transform menuRoot,
            IReadOnlyList<GeneratedToggleEntry> toggles,
            IEnumerable<ToggleClusterGroup> groups)
        {
            var byId = toggles.Select((toggle, index) => new { id = index + 1, toggle })
                .ToDictionary(entry => entry.id, entry => entry.toggle);
            var claimed = new HashSet<int>();
            var applied = 0;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Cluster outfit toggles");
            try
            {
                foreach (var group in groups ?? Array.Empty<ToggleClusterGroup>())
                {
                    if (string.IsNullOrWhiteSpace(group.label) || group.ids == null) continue;
                    var label = group.label.Trim();
                    if (label.EndsWith(" Toggle", StringComparison.OrdinalIgnoreCase)) label = label.Substring(0, label.Length - " Toggle".Length);
                    if (label.Length > 32) continue;
                    var members = group.ids.Distinct().Where(id => !claimed.Contains(id) && byId.ContainsKey(id))
                        .Select(id => new KeyValuePair<int, GeneratedToggleEntry>(id, byId[id])).ToList();
                    foreach (var cluster in SafeClusters(members))
                    {
                        var primary = cluster[0].Value;
                        Undo.RecordObject(primary.objectToggle, "Cluster outfit toggles");
                        Undo.RecordObject(primary.menuItem, "Cluster outfit toggles");
                        Undo.RecordObject(primary.gameObject, "Cluster outfit toggles");
                        var paths = new HashSet<string>(primary.objectToggle.Objects
                            .Select(entry => entry.Object?.referencePath)
                            .Where(path => !string.IsNullOrEmpty(path)));
                        foreach (var member in cluster.Skip(1))
                        {
                            foreach (var entry in member.Value.objectToggle.Objects)
                            {
                                var path = entry.Object?.referencePath;
                                if (!string.IsNullOrEmpty(path) && !paths.Add(path)) continue;
                                primary.objectToggle.Objects.Add(entry);
                            }
                        }

                        primary.menuItem.label = label;
                        primary.gameObject.name = label;
                        EditorUtility.SetDirty(primary.objectToggle);
                        EditorUtility.SetDirty(primary.menuItem);
                        foreach (var member in cluster.Skip(1)) Undo.DestroyObjectImmediate(member.Value.gameObject);
                        foreach (var member in cluster) claimed.Add(member.Key);
                        applied++;
                    }
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            Selection.activeGameObject = menuRoot.gameObject;
            Debug.Log($"Clustered {applied} outfit toggle group(s).", menuRoot.gameObject);
        }

        private static IEnumerable<List<KeyValuePair<int, GeneratedToggleEntry>>> SafeClusters(
            IEnumerable<KeyValuePair<int, GeneratedToggleEntry>> members)
        {
            return members.GroupBy(member => ClusterKey(member.Value), StringComparer.OrdinalIgnoreCase)
                .Select(cluster => cluster.ToList())
                .Where(cluster => cluster.Count > 1 && !HasConflictingLocations(cluster));
        }

        private static string ClusterKey(GeneratedToggleEntry toggle)
        {
            return ClusterKey(string.IsNullOrWhiteSpace(toggle.menuItem.label) ? toggle.gameObject.name : toggle.menuItem.label);
        }

        private static string ClusterKey(string label)
        {
            var words = HumanizeName(label)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 1 && words[0].Length == 1 ? words[1] : words.FirstOrDefault() ?? string.Empty;
        }

        private static bool HasConflictingLocations(IEnumerable<KeyValuePair<int, GeneratedToggleEntry>> members)
        {
            return HasConflictingLocations(members.Select(member => member.Value.menuItem.label ?? member.Value.gameObject.name));
        }

        private static bool HasConflictingLocations(IEnumerable<string> labels)
        {
            var locations = new[] { "ankle", "leg", "waist", "top", "tops", "bottom", "bottoms", "left", "right", "upper", "lower", "front", "back", "shoulder" };
            var variants = labels.Select(label => HumanizeName(label)
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(word => locations.Any(location => string.Equals(location, word, StringComparison.OrdinalIgnoreCase))))
                .Where(word => !string.IsNullOrEmpty(word))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return variants.Count > 1;
        }

        private static ModularAvatarMenuItem FindGeneratedToggleFor(Transform menuRoot, GameObject target)
        {
            var path = new AvatarObjectReference(target).referencePath;
            var objectToggle = menuRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true)
                .FirstOrDefault(toggle => IsGeneratedToggle(toggle) &&
                                           toggle.Objects.Any(entry => entry.Object != null && entry.Object.referencePath == path));
            return objectToggle == null ? null : objectToggle.GetComponent<ModularAvatarMenuItem>();
        }

        private static void StartNameCleanup(
            Transform menuRoot,
            string avatarName,
            IReadOnlyList<Transform> outfitRoots,
            IEnumerable<GameObject> targets,
            bool showErrors)
        {
            var menuItems = targets
                .Select(target => new KeyValuePair<GameObject, ModularAvatarMenuItem>(target, FindGeneratedToggleFor(menuRoot, target)))
                .Where(entry => entry.Value != null)
                .ToList();
            if (menuItems.Count == 0)
            {
                if (showErrors)
                    EditorUtility.DisplayDialog("Clean toggle names", "None of the selected outfit's generated toggles were found.", "OK");
                return;
            }

            var outfitNames = string.Join(", ", outfitRoots.Select(OutfitMenuName));
            var outfitPaths = string.Join("\n", outfitRoots.Select(OutfitPrefabPath));
            StartNameCleanup(menuRoot, avatarName, outfitNames, outfitPaths, menuItems, showErrors);
        }

        private static void CleanMenuNames(VRCAvatarDescriptor avatar, Transform menuRoot)
        {
            if (!AppleIntelligenceNameCleaner.IsAvailable())
            {
                EditorUtility.DisplayDialog("Apple Intelligence unavailable", "Enable Apple Intelligence before cleaning this menu.", "OK");
                return;
            }

            var menuItems = MenuToggleEntries(menuRoot, generatedOnly: false)
                .Select(entry => new KeyValuePair<GameObject, ModularAvatarMenuItem>(entry.gameObject, entry.menuItem))
                .ToList();
            StartNameCleanup(
                menuRoot,
                avatar.gameObject.name,
                menuRoot.name,
                AnimationUtility.CalculateTransformPath(menuRoot, avatar.transform),
                menuItems,
                showErrors: true);
        }

        private static void StartNameCleanup(
            Transform menuRoot,
            string avatarName,
            string outfitNames,
            string outfitPaths,
            List<KeyValuePair<GameObject, ModularAvatarMenuItem>> menuItems,
            bool showErrors)
        {
            if (menuItems.Count == 0)
            {
                if (showErrors) EditorUtility.DisplayDialog("Clean toggle names", "This menu has no child MA Object Toggles.", "OK");
                return;
            }

            var candidates = new List<ToggleNameCandidate>
            {
                new ToggleNameCandidate { id = 0, path = "__outfit_menu__", name = outfitNames },
            };
            candidates.AddRange(menuItems.Select((entry, index) => new ToggleNameCandidate
            {
                id = index + 1,
                path = new AvatarObjectReference(entry.Key).referencePath,
                name = string.IsNullOrWhiteSpace(entry.Value.label) ? entry.Key.name : entry.Value.label,
            }));
            AppleIntelligenceNameCleaner.Clean(
                avatarName,
                outfitNames,
                outfitPaths,
                candidates.ToArray(),
                labels => ApplyCleanedNames(menuRoot, menuItems, labels),
                error =>
                {
                    ApplyCleanedNames(menuRoot, menuItems, LocalCleanupLabels(menuRoot.name, menuItems));
                    var message = $"{error}\n\nApplied basic local cleanup instead.";
                    if (showErrors) EditorUtility.DisplayDialog("Apple Intelligence cleanup skipped", message, "OK");
                    else Debug.LogWarning($"Apple Intelligence skipped automatic cleanup: {message}", menuRoot.gameObject);
                });
        }

        private static string OutfitPrefabPath(Transform outfitRoot)
        {
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outfitRoot.gameObject);
            return string.IsNullOrEmpty(path) ? AssetDatabase.GetAssetPath(outfitRoot.gameObject) : path;
        }

        private static string OutfitMenuName(Transform outfitRoot)
        {
            return OutfitTitle(OutfitPrefabPath(outfitRoot), outfitRoot.name);
        }

        private static string OutfitTitle(string assetPath, string fallbackName)
        {
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (IsGenericOutfitName(name)) name = OutfitFolderName(assetPath) ?? name;
            name = string.IsNullOrWhiteSpace(name) ? fallbackName : name;
            return StripKnownBoothBaseNames(HumanizeName(name));
        }

        private static string OutfitFolderName(string assetPath)
        {
            for (var folder = Path.GetDirectoryName(assetPath); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
            {
                var name = Path.GetFileName(folder);
                if (IsGenericOutfitName(name) || IsAssetFolder(name) || IsKnownBoothBaseName(name)) continue;
                return name;
            }

            return null;
        }

        private static bool IsGenericOutfitName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            var words = HumanizeName(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 1 && (string.Equals(words[0], "Color", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(words[0], "Outfit", StringComparison.OrdinalIgnoreCase)) ||
                   words.Length == 2 && string.Equals(words[0], "Color", StringComparison.OrdinalIgnoreCase) && int.TryParse(words[1], out _);
        }

        private static bool IsAssetFolder(string name)
        {
            return new[] { "Assets", "Prefab", "Prefeb", "Fbx", "Model", "Models", "Avatar", "Avatars" }
                .Any(folder => string.Equals(name, folder, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsKnownBoothBaseName(string name)
        {
            return KnownBoothBaseNames.Any(baseName => string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase));
        }

        private static string HumanizeName(string name)
        {
            name = name.Replace('_', ' ').Replace('-', ' ');
            return string.Concat(name.Select((character, index) =>
                index > 0 && char.IsUpper(character) && char.IsLower(name[index - 1]) ? " " + character : character.ToString())).Trim();
        }

        private static string StripKnownBoothBaseNames(string name)
        {
            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var baseName in KnownBoothBaseNames)
            {
                var baseWords = baseName.Split(' ');
                for (var index = words.Count - baseWords.Length; index >= 0; index--)
                {
                    if (!baseWords.Select((word, offset) => string.Equals(words[index + offset], word, StringComparison.OrdinalIgnoreCase)).All(matches => matches)) continue;
                    words.RemoveRange(index, baseWords.Length);
                }
            }

            return words.Count == 0 ? name : string.Join(" ", words);
        }

        private static string CleanGeneratedLabel(string label, string outfitName, string sourceName)
        {
            label = HumanizeName(label);
            while (label.StartsWith(outfitName + " ", StringComparison.OrdinalIgnoreCase))
                label = label.Substring(outfitName.Length).Trim();
            label = StripKnownBoothBaseNames(label);
            label = StripCategoryPrefix(label, sourceName);

            if (!HasAccessorySuffix(sourceName)) return label;
            foreach (var suffix in new[] { " Accessories", " Accessory", " Accent", " Accs", " Ascs", " Access", " Acc", " Asc", " Acs" })
                if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return label.Substring(0, label.Length - suffix.Length).Trim() + " Accessory";
            return label + " Accessory";
        }

        private static bool HasAccessorySuffix(string name)
        {
            name = HumanizeName(name);
            return new[] { " Accs", " Ascs", " Access", " Acc", " Asc", " Acs" }
                .Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static string StripCategoryPrefix(string label, string sourceName)
        {
            sourceName = sourceName.Trim();
            if (sourceName.Length < 3 || !char.IsLetter(sourceName[0]) || (sourceName[1] != '_' && sourceName[1] != '-')) return label;
            var prefix = sourceName[0] + " ";
            return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? label.Substring(prefix.Length).Trim() : label;
        }

        private static Dictionary<int, string> LocalCleanupLabels(
            string outfitName,
            IEnumerable<KeyValuePair<GameObject, ModularAvatarMenuItem>> menuItems)
        {
            var labels = new Dictionary<int, string> { { 0, outfitName } };
            var index = 1;
            foreach (var entry in menuItems)
                labels[index++] = CleanGeneratedLabel(entry.Key.name, outfitName, entry.Key.name);
            return labels;
        }

        private static void ApplyCleanedNames(
            Transform menuRoot,
            IEnumerable<KeyValuePair<GameObject, ModularAvatarMenuItem>> menuItems,
            Dictionary<int, string> labels)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clean outfit toggle names");
            var renamed = 0;

            if (labels.TryGetValue(0, out var menuName))
            {
                menuName = menuName.Trim();
                var menuItem = menuRoot.GetComponent<ModularAvatarMenuItem>();
                if (!string.IsNullOrEmpty(menuName) && menuName.Length <= 32 && menuItem != null)
                {
                    Undo.RecordObject(menuRoot.gameObject, "Clean outfit toggle menu name");
                    Undo.RecordObject(menuItem, "Clean outfit toggle menu name");
                    menuRoot.name = menuName;
                    menuItem.label = menuName;
                    EditorUtility.SetDirty(menuItem);
                    renamed++;
                }
            }

            var index = 1;
            foreach (var entry in menuItems)
            {
                if (!labels.TryGetValue(index++, out var label) || entry.Value == null) continue;

                label = CleanGeneratedLabel(label, menuRoot.name, entry.Key.name);
                if (string.IsNullOrEmpty(label) || label.Length > 32) continue;
                Undo.RecordObject(entry.Value, "Clean outfit toggle name");
                Undo.RecordObject(entry.Value.gameObject, "Clean outfit toggle name");
                entry.Value.label = label;
                entry.Value.gameObject.name = label;
                EditorUtility.SetDirty(entry.Value);
                renamed++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"Cleaned {renamed} outfit toggle name(s).");
        }

        private static Texture2D SaveIcon(Texture2D icon, GameObject target, string assetPath = null)
        {
            if (icon == null) return null;

            try
            {
                Directory.CreateDirectory(IconFolder);
                if (string.IsNullOrEmpty(assetPath))
                    assetPath = AssetDatabase.GenerateUniqueAssetPath($"{IconFolder}/{SafeFileName(target.name)}.png");
                File.WriteAllBytes(assetPath, icon.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = IconSize;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not create an icon for '{target.name}': {exception.Message}", target);
                return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(icon);
            }
        }

        private static Dictionary<GameObject, Texture2D> RenderIcons(GameObject avatarRoot, IEnumerable<GameObject> targets)
        {
            return RenderIconGroups(avatarRoot, targets.Distinct()
                .Select(target => new KeyValuePair<GameObject, IEnumerable<GameObject>>(target, new[] { target })));
        }

        private static Dictionary<TKey, Texture2D> RenderIconGroups<TKey>(
            GameObject avatarRoot,
            IEnumerable<KeyValuePair<TKey, IEnumerable<GameObject>>> groups)
        {
            var icons = new Dictionary<TKey, Texture2D>();
            var sourceGroups = groups.Select(group => new KeyValuePair<TKey, List<GameObject>>(
                    group.Key,
                    group.Value.Where(target => target != null).Distinct().ToList()))
                .Where(group => group.Value.Count > 0)
                .ToList();
            if (sourceGroups.Count == 0) return icons;

            var preview = new PreviewRenderUtility();
            var avatarCopy = UnityEngine.Object.Instantiate(avatarRoot);

            try
            {
                avatarCopy.name = avatarRoot.name;
                avatarCopy.SetActive(true);
                preview.AddSingleGO(avatarCopy);
                var renderers = avatarCopy.GetComponentsInChildren<Renderer>(true);

                foreach (var group in sourceGroups)
                {
                    try
                    {
                        var targetCopies = group.Value.Select(target =>
                        {
                            var path = AnimationUtility.CalculateTransformPath(target.transform, avatarRoot.transform);
                            return string.IsNullOrEmpty(path) ? avatarCopy.transform : avatarCopy.transform.Find(path);
                        }).Where(target => target != null).ToList();
                        if (targetCopies.Count == 0) continue;

                        foreach (var targetCopy in targetCopies) SetAncestorsActive(targetCopy, avatarCopy.transform);
                        var bounds = ShowOnly(renderers, targetCopies);
                        if (bounds.size == Vector3.zero) continue;
                        icons[group.Key] = CaptureIcon(preview, avatarCopy.transform, bounds);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Could not render an icon for '{group.Value[0].name}': {exception.Message}", group.Value[0]);
                    }
                }
            }
            finally
            {
                preview.Cleanup();
            }

            return icons;
        }

        private static void ConfigurePreview(PreviewRenderUtility preview, Transform avatarRoot, float yaw = 180f)
        {
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            camera.transform.rotation = avatarRoot.rotation * Quaternion.Euler(10f, yaw, 0f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
            preview.lights[1].intensity = 1.0f;
        }

        private static Texture2D Capture(PreviewRenderUtility preview, Bounds bounds)
        {
            var camera = preview.camera;
            camera.aspect = 1f;
            camera.transform.position = bounds.center - camera.transform.forward * (CameraDistance(camera, bounds) * 1.05f);
            var renderTexture = RenderTexture.GetTemporary(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                icon.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                icon.Apply();
                return icon;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static Texture2D CaptureIcon(PreviewRenderUtility preview, Transform avatarRoot, Bounds bounds)
        {
            ConfigurePreview(preview, avatarRoot);
            var best = Capture(preview, bounds);
            var bestPixels = ContentPixelCount(best);
            if (bestPixels < IconSize * IconSize / 50)
            {
                foreach (var yaw in new[] { 90f, -90f })
                {
                    ConfigurePreview(preview, avatarRoot, yaw);
                    var candidate = Capture(preview, bounds);
                    var candidatePixels = ContentPixelCount(candidate);
                    if (candidatePixels > bestPixels)
                    {
                        UnityEngine.Object.DestroyImmediate(best);
                        best = candidate;
                        bestPixels = candidatePixels;
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(candidate);
                    }
                }
            }

            return FitIconToContent(best);
        }

        private static Texture2D FitIconToContent(Texture2D icon)
        {
            var content = ContentBounds(icon.GetPixels32(), IconSize, IconSize);
            if (content.width == 0 || content.height == 0) return icon;

            var scale = Mathf.Min(IconSize * 0.875f / content.width, IconSize * 0.875f / content.height);
            if (scale <= 1f) return icon;

            var width = Mathf.Max(1, Mathf.RoundToInt(content.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(content.height * scale));
            var fitted = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            var source = icon.GetPixels();
            var pixels = new Color[IconSize * IconSize];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sourceX = Mathf.Clamp(content.x + (x + 0.5f) * content.width / width - 0.5f, content.x, content.xMax - 1);
                var sourceY = Mathf.Clamp(content.y + (y + 0.5f) * content.height / height - 0.5f, content.y, content.yMax - 1);
                var x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), content.x, content.xMax - 1);
                var y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), content.y, content.yMax - 1);
                var x1 = Mathf.Min(x0 + 1, content.xMax - 1);
                var y1 = Mathf.Min(y0 + 1, content.yMax - 1);
                var xLerp = sourceX - x0;
                var yLerp = sourceY - y0;
                var color = Color.Lerp(Color.Lerp(source[y0 * IconSize + x0], source[y0 * IconSize + x1], xLerp),
                    Color.Lerp(source[y1 * IconSize + x0], source[y1 * IconSize + x1], xLerp), yLerp);
                pixels[((IconSize - height) / 2 + y) * IconSize + (IconSize - width) / 2 + x] = color;
            }
            fitted.SetPixels(pixels);
            fitted.Apply();
            UnityEngine.Object.DestroyImmediate(icon);
            return fitted;
        }

        private static int ContentPixelCount(Texture2D icon)
        {
            return icon.GetPixels32().Count(pixel => pixel.a > 8);
        }

        private static RectInt ContentBounds(Color32[] pixels, int width, int height)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= 8) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }

            return maxX < minX ? new RectInt() : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static void SetAncestorsActive(Transform target, Transform root)
        {
            for (var current = target; current != null; current = current.parent)
            {
                current.gameObject.SetActive(true);
                if (current == root) return;
            }
        }

        private static Bounds ShowOnly(IEnumerable<Renderer> renderers, IEnumerable<Transform> targets)
        {
            var targetList = targets.ToList();
            var bounds = new Bounds();
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
                renderer.enabled = targetList.Any(target => renderer.transform == target || renderer.transform.IsChildOf(target));
                if (!renderer.enabled) continue;
                var rendererBounds = GeometryBounds(renderer);
                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return bounds;
        }

        private static float CameraDistance(Camera camera, Bounds bounds)
        {
            var inverseRotation = Quaternion.Inverse(camera.transform.rotation);
            var maxX = 0f;
            var maxY = 0f;
            var maxZ = 0f;
            foreach (var corner in BoundsCorners(bounds))
            {
                var local = inverseRotation * (corner - bounds.center);
                maxX = Mathf.Max(maxX, Mathf.Abs(local.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(local.y));
                maxZ = Mathf.Max(maxZ, Mathf.Abs(local.z));
            }

            var halfVerticalFov = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            var halfHorizontalFov = Mathf.Atan(Mathf.Tan(halfVerticalFov) * camera.aspect);
            return Mathf.Max(maxY / Mathf.Tan(halfVerticalFov), maxX / Mathf.Tan(halfHorizontalFov)) + maxZ;
        }

        private static Bounds GeometryBounds(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (renderer is MeshRenderer) mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return mesh == null ? renderer.bounds : TransformBounds(mesh.bounds, renderer.localToWorldMatrix);
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var corners = BoundsCorners(bounds).Select(matrix.MultiplyPoint3x4).ToArray();
            var transformed = new Bounds(corners[0], Vector3.zero);
            foreach (var corner in corners.Skip(1)) transformed.Encapsulate(corner);
            return transformed;
        }

        private static IEnumerable<Vector3> BoundsCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            yield return new Vector3(min.x, min.y, min.z);
            yield return new Vector3(min.x, min.y, max.z);
            yield return new Vector3(min.x, max.y, min.z);
            yield return new Vector3(min.x, max.y, max.z);
            yield return new Vector3(max.x, min.y, min.z);
            yield return new Vector3(max.x, min.y, max.z);
            yield return new Vector3(max.x, max.y, min.z);
            yield return new Vector3(max.x, max.y, max.z);
        }

        private static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name) ? "Outfit" : name;
        }
    }
}
