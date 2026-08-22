namespace Square.Hosting.Web;

internal static class SquareWebInteractiveRuntime
{
    internal const string Script = """
        (() => {
          const bootstrap = document.currentScript;
          const token = bootstrap.dataset.squareToken;
          let revision = Number(bootstrap.dataset.squareRevision || "0");
          let queued = 0;
          let chain = Promise.resolve();

          for (const type of ["click", "input", "change"]) {
            document.addEventListener(type, event => {
              const domTarget = event.target instanceof Element ? event.target : null;
              const squareTarget = domTarget?.closest("[data-square-id]");
              const listener = domTarget?.closest(`[data-square-events~="${type}"]`);
              if (!squareTarget || !listener) return;

              const sequence = ++queued;
              const link = type === "click" ? domTarget.closest("a[href]")?.href : null;
              if (type === "click") event.preventDefault();

              const valueTarget = domTarget.matches("input,textarea,select")
                ? domTarget
                : squareTarget.matches("input,textarea,select") ? squareTarget : null;
              const payload = {
                token,
                revision: 0,
                elementId: Number(squareTarget.dataset.squareId),
                type,
                value: valueTarget?.value ?? null,
                checked: valueTarget instanceof HTMLInputElement ? valueTarget.checked : null
              };

              chain = chain.then(async () => {
                payload.revision = revision;
                const response = await fetch(location.href, {
                  method: "POST",
                  credentials: "same-origin",
                  headers: { "Content-Type": "application/json" },
                  body: JSON.stringify(payload)
                });
                if (response.status === 409 || response.status === 410) {
                  location.reload();
                  return;
                }
                if (!response.ok) throw new Error(`Square event failed: ${response.status}`);

                const update = await response.json();
                revision = update.revision;
                if (sequence === queued) applyUpdate(update);
                if (type === "click" && link && !update.defaultPrevented) location.assign(link);
              }).catch(error => {
                document.documentElement.dataset.squareInteractionError = "true";
                console.error(error);
              });
            }, true);
          }

          function applyUpdate(update) {
            const currentRoot = document.querySelector(".square-root");
            if (!currentRoot) return;

            const active = document.activeElement instanceof Element ? document.activeElement : null;
            const focused = active?.closest("[data-square-id]")?.dataset.squareId ?? null;
            const selectionStart = "selectionStart" in (active ?? {}) ? active.selectionStart : null;
            const selectionEnd = "selectionEnd" in (active ?? {}) ? active.selectionEnd : null;
            const windowScroll = [window.scrollX, window.scrollY];
            const scrollPositions = new Map();
            for (const element of document.querySelectorAll("[data-square-id]")) {
              if (element.scrollLeft || element.scrollTop)
                scrollPositions.set(element.dataset.squareId, [element.scrollLeft, element.scrollTop]);
            }

            const template = document.createElement("template");
            template.innerHTML = update.bodyHtml.trim();
            const nextRoot = template.content.querySelector(".square-root");
            if (!nextRoot) return;
            currentRoot.replaceWith(nextRoot);

            let style = document.querySelector("style[data-square-css=\"true\"]");
            if (!style) {
              style = document.createElement("style");
              style.dataset.squareCss = "true";
              document.head.append(style);
            }
            style.textContent = update.css;

            for (const [id, position] of scrollPositions) {
              const element = document.querySelector(`[data-square-id="${id}"]`);
              if (element) [element.scrollLeft, element.scrollTop] = position;
            }
            if (focused) {
              const element = document.querySelector(`[data-square-id="${focused}"]`);
              const focusTarget = element?.matches("input,textarea,select,button,a[href]")
                ? element
                : element?.querySelector("input,textarea,select,button,a[href]");
              focusTarget?.focus({ preventScroll: true });
              if (selectionStart !== null && "setSelectionRange" in (focusTarget ?? {}))
                focusTarget.setSelectionRange(selectionStart, selectionEnd);
            }
            window.scrollTo(...windowScroll);
          }
        })();
        """;
}
