window.syncTaskDnd = window.syncTaskDnd || {
  attachTableDropZone: function (tbodyId, dotNetRef) {
    const tbody = document.getElementById(tbodyId);
    if (!tbody || !dotNetRef) {
      console.warn("[DND:js:attach] tbody or dotNetRef missing", { tbodyId });
      return;
    }

    if (tbody.__syncTaskDndHandlers) {
      return;
    }

    const resolveIndex = (event) => {
      if (!event || !event.target || !event.target.closest) {
        return null;
      }

      const row = event.target.closest("tr[data-row-index]");
      if (!row) {
        return null;
      }

      const raw = row.getAttribute("data-row-index");
      const index = Number.parseInt(raw || "", 10);
      return Number.isNaN(index) ? null : index;
    };

    const onDragOver = (event) => {
      event.preventDefault();

      const index = resolveIndex(event);
      if (index === null) {
        return;
      }

      dotNetRef.invokeMethodAsync("NotifyDragOverIndex", index);
    };

    const onDrop = (event) => {
      event.preventDefault();

      const index = resolveIndex(event);
      if (index === null) {
        console.warn("[DND:js:drop] target row not found");
        return;
      }

      console.info("[DND:js:drop] notify index", { index });
      dotNetRef.invokeMethodAsync("NotifyDropIndex", index);
    };

    tbody.addEventListener("dragover", onDragOver);
    tbody.addEventListener("drop", onDrop);
    tbody.__syncTaskDndHandlers = { onDragOver, onDrop, dotNetRef, resolveIndex };

    console.info("[DND:js:attach] attached", { tbodyId });
  },

  startPointerDrag: function (event) {
    try {
      const target = event && event.target;
      const row = target && target.closest ? target.closest("tr[data-row-index]") : null;
      const tbody = row && row.closest ? row.closest("tbody") : null;
      const handlers = tbody && tbody.__syncTaskDndHandlers ? tbody.__syncTaskDndHandlers : null;
      if (!row || !tbody || !handlers || !handlers.dotNetRef) {
        console.warn("[DND:js:pointer-start] missing row/tbody/handlers");
        return;
      }

      const raw = row.getAttribute("data-row-index");
      const sourceIndex = Number.parseInt(raw || "", 10);
      if (Number.isNaN(sourceIndex)) {
        console.warn("[DND:js:pointer-start] invalid source index", { raw });
        return;
      }

      const state = {
        active: true,
        lastIndex: sourceIndex,
        tbody,
        dotNetRef: handlers.dotNetRef
      };

      window.syncTaskDnd.__pointerState = state;
      state.dotNetRef.invokeMethodAsync("NotifyPointerDragStart", sourceIndex);

      const resolveIndexFromPoint = (clientX, clientY) => {
        const el = document.elementFromPoint(clientX, clientY);
        if (!el || !el.closest) {
          return null;
        }

        const rowAtPoint = el.closest("tr[data-row-index]");
        if (!rowAtPoint) {
          return null;
        }

        const value = Number.parseInt(rowAtPoint.getAttribute("data-row-index") || "", 10);
        return Number.isNaN(value) ? null : value;
      };

      const onPointerMove = (moveEvent) => {
        if (!state.active) {
          return;
        }

        const index = resolveIndexFromPoint(moveEvent.clientX, moveEvent.clientY);
        if (index === null) {
          return;
        }

        if (index !== state.lastIndex) {
          state.lastIndex = index;
          state.dotNetRef.invokeMethodAsync("NotifyDragOverIndex", index);
        }
      };

      const cleanup = () => {
        window.removeEventListener("pointermove", onPointerMove, true);
        window.removeEventListener("pointerup", onPointerUp, true);
        window.removeEventListener("pointercancel", onPointerCancel, true);
        if (window.syncTaskDnd.__pointerState === state) {
          window.syncTaskDnd.__pointerState = null;
        }
      };

      const onPointerUp = (upEvent) => {
        if (!state.active) {
          cleanup();
          return;
        }

        state.active = false;
        const dropIndex = resolveIndexFromPoint(upEvent.clientX, upEvent.clientY);

        if (dropIndex === null) {
          console.info("[DND:js:pointer-up] cancel(no target)");
          state.dotNetRef.invokeMethodAsync("NotifyPointerDragCancel");
          cleanup();
          return;
        }

        console.info("[DND:js:pointer-up] drop", { dropIndex });
        state.dotNetRef.invokeMethodAsync("NotifyDropIndex", dropIndex);
        cleanup();
      };

      const onPointerCancel = () => {
        if (state.active) {
          state.active = false;
          state.dotNetRef.invokeMethodAsync("NotifyPointerDragCancel");
        }

        cleanup();
      };

      window.addEventListener("pointermove", onPointerMove, true);
      window.addEventListener("pointerup", onPointerUp, true);
      window.addEventListener("pointercancel", onPointerCancel, true);

      console.info("[DND:js:pointer-start] started", { sourceIndex });
      event.preventDefault();
    } catch (error) {
      console.error("[DND:js:pointer-start] failed", error);
    }
  },

  logPointerDown: function (event) {
    try {
      const target = event && event.target;
      const tagName = target && target.tagName ? target.tagName : "";
      console.info("[DND:js:pointerdown]", { tagName, buttons: event && event.buttons });
    } catch {
      // no-op
    }
  },

  setDragData: function (event) {
    if (!event || !event.dataTransfer) {
      console.warn("[DND:js:dragstart] dataTransfer is missing", event);
      return;
    }

    try {
      event.dataTransfer.setData("text/plain", "sync-task-row");
      event.dataTransfer.effectAllowed = "move";
      console.info("[DND:js:dragstart] setData done", {
        effectAllowed: event.dataTransfer.effectAllowed,
        types: event.dataTransfer.types
      });
    } catch {
      console.error("[DND:js:dragstart] setData failed");
      // no-op
    }
  }
};
