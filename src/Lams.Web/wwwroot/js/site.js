// LAMS — small site script. Loaded as an external file (CSP: script-src 'self'); no inline handlers anywhere.
(function () {
  "use strict";

  // Send the antiforgery token with every non-GET htmx request (validated by AutoValidateAntiforgeryToken).
  document.addEventListener("htmx:configRequest", function (event) {
    var verb = (event.detail.verb || "get").toLowerCase();
    if (verb === "get") {
      return;
    }
    var meta = document.querySelector('meta[name="request-verification-token"]');
    if (meta) {
      event.detail.headers["RequestVerificationToken"] = meta.getAttribute("content");
    }
  });

  // Modals: close on Escape or on any [data-close-modal] element; move focus into the dialog when it opens.
  // A click on the backdrop itself (outside the dialog) also closes an htmx-loaded modal.
  document.addEventListener("click", function (event) {
    var closer = event.target.closest("[data-close-modal]");
    var host = document.getElementById("modal-host");
    var onBackdrop = event.target.classList && event.target.classList.contains("modal-backdrop")
      && host && host.contains(event.target);
    if (closer || onBackdrop) {
      event.preventDefault();
      closeModal();
    }
  });

  // Copy buttons: <button data-copy="#selector" data-copied="Copied">. The page still works without the clipboard API
  // (the secret is selectable).
  document.addEventListener("click", function (event) {
    var button = event.target.closest("[data-copy]");
    if (!button || !navigator.clipboard) {
      return;
    }
    var source = document.querySelector(button.getAttribute("data-copy"));
    if (!source) {
      return;
    }
    navigator.clipboard.writeText(source.textContent.trim()).then(function () {
      showToast(button.getAttribute("data-copied") || "Copied", "success");
    });
  });

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape") {
      closeModal();
    }
  });

  document.addEventListener("htmx:afterSwap", function (event) {
    var dialog = event.detail.target.querySelector("[role='dialog']");
    if (dialog) {
      var focusable = dialog.querySelector("[autofocus], input, select, textarea, button");
      (focusable || dialog).focus();
    }
  });

  // Toasts: server responses can trigger {"showToast": {"message": "...", "tone": "success"}} via HX-Trigger.
  document.body.addEventListener("showToast", function (event) {
    if (event.detail) {
      showToast(event.detail.message, event.detail.tone);
    }
  });

  function showToast(message, tone) {
    var host = document.getElementById("toast-host");
    if (!host) {
      return;
    }
    var toast = document.createElement("div");
    toast.className = "toast " + (tone || "");
    toast.setAttribute("role", "status");
    toast.textContent = message || "";
    host.appendChild(toast);
    setTimeout(function () { toast.remove(); }, 4000);
  }

  function closeModal() {
    var host = document.getElementById("modal-host");
    if (host && host.firstChild) {
      host.innerHTML = "";
    }
  }
})();
