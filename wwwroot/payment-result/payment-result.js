(() => {
    const orderId = new URLSearchParams(window.location.search).get("orderId");
    const resultMark = document.getElementById("result-mark");
    const eyebrow = document.getElementById("result-eyebrow");
    const title = document.getElementById("result-title");
    const message = document.getElementById("result-message");
    const footnote = document.getElementById("result-footnote");
    const orderLine = document.getElementById("order-line");
    const orderReference = document.getElementById("order-reference");
    const retryButton = document.getElementById("retry-button");
    const pollIntervalMs = 2000;
    const pollTimeoutMs = 90000;
    let isChecking = false;

    function render(state, heading, details, options = {}) {
        resultMark.dataset.state = state;
        eyebrow.textContent = options.eyebrow ?? "PAYMENT UPDATE";
        title.textContent = heading;
        message.textContent = details;
        footnote.textContent = options.footnote ?? "Payment confirmation is provided by the payment processor.";
        retryButton.hidden = !options.canRetry;
    }

    function delay(milliseconds) {
        return new Promise(resolve => window.setTimeout(resolve, milliseconds));
    }

    async function checkStatus() {
        if (!orderId) {
            render(
                "error",
                "Order reference missing",
                "We could not identify this payment. Please return to the store and check your order.",
                { eyebrow: "UNABLE TO CHECK", footnote: "No payment status was inferred from the browser redirect." }
            );
            return;
        }

        if (isChecking) return;
        isChecking = true;
        retryButton.hidden = true;
        orderReference.textContent = orderId;
        orderLine.hidden = false;

        const deadline = Date.now() + pollTimeoutMs;
        const statusUrl = `/api/payments/orders/${encodeURIComponent(orderId)}/status`;

        try {
            while (Date.now() < deadline) {
                try {
                    const response = await fetch(statusUrl, {
                        headers: { Accept: "application/json" },
                        cache: "no-store"
                    });

                    if (response.status === 404) {
                        render(
                            "error",
                            "Order not found",
                            "We could not find this order. Please contact the store before trying the payment again.",
                            { eyebrow: "ORDER NOT FOUND" }
                        );
                        return;
                    }

                    if (!response.ok) throw new Error("status_unavailable");

                    const order = await response.json();
                    if (order.status === "Paid") {
                        render(
                            "paid",
                            "Payment confirmed",
                            "Your payment was successful. The store can now finalize your order.",
                            { eyebrow: "PAYMENT COMPLETE" }
                        );
                        return;
                    }

                    if (order.status === "Failed") {
                        render(
                            "failed",
                            "Payment not completed",
                            "The payment was not completed. Check your order with the store before making another attempt.",
                            { eyebrow: "PAYMENT NOT COMPLETED" }
                        );
                        return;
                    }

                    if (order.status !== "Pending") throw new Error("unknown_status");

                    render(
                        "checking",
                        "Confirming your payment",
                        "Your payment is still being confirmed. This page will update automatically.",
                        { eyebrow: "PAYMENT PROCESSING", footnote: "This can take a short while. Please do not pay again yet." }
                    );
                } catch {
                    render(
                        "checking",
                        "Still checking",
                        "We could not reach the status service just now. We will try again automatically.",
                        { eyebrow: "RETRYING STATUS CHECK" }
                    );
                }

                await delay(pollIntervalMs);
            }

            render(
                "pending",
                "Confirmation is taking longer",
                "Your order may still be processing. Check the status again before attempting another payment.",
                { eyebrow: "STILL PROCESSING", canRetry: true }
            );
        } finally {
            isChecking = false;
        }
    }

    retryButton.addEventListener("click", checkStatus);
    checkStatus();
})();