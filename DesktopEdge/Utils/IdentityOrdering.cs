using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using ZitiDesktopEdge.Models;

namespace Ziti.Desktop.Edge.Utils {

    public enum IdentitySortColumn {
        Custom,
        Enabled,
        Name,
        Services,
    }

    public enum SortDirection {
        Asc,
        Desc,
    }

    public static class IdentityOrdering {

        private static StringCollection OrderList {
            get {
                if (ZitiDesktopEdge.Properties.Settings.Default.IdentityOrder == null) {
                    ZitiDesktopEdge.Properties.Settings.Default.IdentityOrder = new StringCollection();
                    ZitiDesktopEdge.Properties.Settings.Default.Save();
                }
                return ZitiDesktopEdge.Properties.Settings.Default.IdentityOrder;
            }
        }

        public static void EnsureOrderContainsAll(IList<ZitiIdentity> identities) {
            var order = OrderList;
            var liveIds = new HashSet<string>(identities.Where(i => i != null).Select(i => i.Identifier));

            bool changed = false;

            // Remove dead ids
            for (int i = order.Count - 1; i >= 0; i--) {
                if (!liveIds.Contains(order[i])) {
                    order.RemoveAt(i);
                    changed = true;
                }
            }

            // Append new ids
            foreach (var id in identities) {
                if (id?.Identifier == null) continue;
                if (!order.Contains(id.Identifier)) {
                    order.Add(id.Identifier);
                    changed = true;
                }
            }

            if (changed) {
                ZitiDesktopEdge.Properties.Settings.Default.Save();
            }
        }

        private static Dictionary<string, int> BuildIndexMap() {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = OrderList;
            for (int i = 0; i < order.Count; i++) {
                var key = order[i];
                if (!map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        private static string NameKey(ZitiIdentity i) {
            // Use whatever the UI displays; fallback to controller url host-ish; then identifier.
            // Avoid nulls and keep it stable enough for sorting.
            if (!string.IsNullOrWhiteSpace(i?.Name)) return i.Name.Trim();
            if (!string.IsNullOrWhiteSpace(i?.ControllerUrl)) return i.ControllerUrl.Trim();
            return i?.Identifier ?? "";
        }

        private static int ServiceCount(ZitiIdentity i) => i?.Services?.Count ?? 0;

        public static List<ZitiIdentity> ApplySort(
            IList<ZitiIdentity> identities,
            IdentitySortColumn column,
            SortDirection direction
        ) {
            EnsureOrderContainsAll(identities);

            var list = identities.Where(i => i != null).ToList();
            var indexMap = BuildIndexMap();

            int CustomIndex(ZitiIdentity i) =>
                (i?.Identifier != null && indexMap.TryGetValue(i.Identifier, out var idx)) ? idx : int.MaxValue;

            IOrderedEnumerable<ZitiIdentity> ordered;

            switch (column) {
                case IdentitySortColumn.Enabled:
                    ordered =
                        (direction == SortDirection.Asc
                            ? list.OrderBy(i => i.IsEnabled)
                            : list.OrderByDescending(i => i.IsEnabled))
                        // tie-breakers to keep stable ordering:
                        .ThenBy(i => CustomIndex(i))
                        .ThenBy(i => NameKey(i), StringComparer.OrdinalIgnoreCase);
                    break;

                case IdentitySortColumn.Services:
                    ordered =
                        (direction == SortDirection.Asc
                            ? list.OrderBy(i => ServiceCount(i))
                            : list.OrderByDescending(i => ServiceCount(i)))
                        .ThenBy(i => CustomIndex(i))
                        .ThenBy(i => NameKey(i), StringComparer.OrdinalIgnoreCase);
                    break;

                case IdentitySortColumn.Name:
                    ordered =
                        (direction == SortDirection.Asc
                            ? list.OrderBy(i => NameKey(i), StringComparer.OrdinalIgnoreCase)
                            : list.OrderByDescending(i => NameKey(i), StringComparer.OrdinalIgnoreCase))
                        .ThenBy(i => CustomIndex(i));
                    break;

                case IdentitySortColumn.Custom:
                default:
                    ordered = list.OrderBy(i => CustomIndex(i));
                    break;
            }

            return ordered.ToList();
        }

        public static void MoveInCustomOrder(string draggedIdentifier, string targetIdentifier, bool insertBefore) {
            if (string.IsNullOrEmpty(draggedIdentifier) || string.IsNullOrEmpty(targetIdentifier)) return;
            if (draggedIdentifier == targetIdentifier) return;

            var order = OrderList;
            var items = order.Cast<string>().ToList();

            int from = items.IndexOf(draggedIdentifier);
            int to = items.IndexOf(targetIdentifier);
            if (from < 0 || to < 0) return;

            items.RemoveAt(from);

            // adjust target index if removal shifts it
            if (from < to) to--;

            int insertIndex = insertBefore ? to : to + 1;
            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > items.Count) insertIndex = items.Count;

            items.Insert(insertIndex, draggedIdentifier);

            // write back
            order.Clear();
            foreach (var id in items) order.Add(id);

            ZitiDesktopEdge.Properties.Settings.Default.Save();
        }
    }
}