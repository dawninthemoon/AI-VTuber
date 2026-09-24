const chat = document.getElementById("chat");
const input = document.getElementById("message");
const sendButton = document.getElementById("sendButton");
const resetButton = document.getElementById("resetButton");
const interruptButton = document.getElementById("interruptButton");

let isSending = false;

input.addEventListener("keydown", event => {
    if (
        event.key === "Enter" &&
        !event.isComposing &&
        !isSending
    ) {
        event.preventDefault();
        sendMessage();
    }
});

sendButton.addEventListener("click", sendMessage);
resetButton.addEventListener("click", resetChat);
interruptButton.addEventListener("click", interruptSpeech);

async function sendMessage() {
    const message = input.value.trim();

    if (!message || isSending) {
        return;
    }

    isSending = true;
    setSendingState(true);

    addMessage(message, "user");

    input.value = "";
    input.focus();

    const thinkingBubble = addMessage(".", "ai", true);
    const stopThinkingAnimation =
        startThinkingAnimation(thinkingBubble);

    try {
        const response = await fetch("/chat", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                message
            })
        });

        if (!response.ok) {
            const errorText = await response.text();

            let errorMessage = "AI 응답 생성에 실패했습니다.";
            try {
                const problem = JSON.parse(errorText);
                errorMessage = problem.detail || problem.error || problem.title || errorMessage;
            }
            catch {
                if (errorText) {
                    errorMessage = errorText;
                }
            }

            throw new Error(
                errorMessage);
        }

        const result = await response.json();

        stopThinkingAnimation();

        thinkingBubble.classList.remove("thinking");

        const aiText =
            result.response ||
            "응답이 비어 있습니다.";

        thinkingBubble.textContent = aiText;

        if (Array.isArray(result.sources) && result.sources.length > 0) {
            const sources = document.createElement("div");
            sources.classList.add("sources");

            for (const source of result.sources) {
                const link = document.createElement("a");
                link.href = source.url;
                link.target = "_blank";
                link.rel = "noopener noreferrer";
                link.textContent = source.title;
                sources.appendChild(link);
            }

            thinkingBubble.appendChild(sources);
        }

        //speak(aiText);

        scrollToBottom();
    }
    catch (error) {
        stopThinkingAnimation();

        thinkingBubble.classList.remove("thinking");

        thinkingBubble.textContent = error instanceof TypeError
            ? "서버와 연결할 수 없습니다."
            : error.message;

        console.error(error);
    }
    finally {
        isSending = false;
        setSendingState(false);
        input.focus();
    }
}

function addMessage(
    text,
    type,
    thinking = false
) {
    const bubble =
        document.createElement("div");

    bubble.classList.add(
        "message",
        type
    );

    if (thinking) {
        bubble.classList.add("thinking");
    }

    bubble.textContent = text;

    chat.appendChild(bubble);

    scrollToBottom();

    return bubble;
}

function startThinkingAnimation(bubble) {
    let dotCount = 1;

    const intervalId = setInterval(() => {
        dotCount = (dotCount % 3) + 1;

        bubble.textContent =
            ".".repeat(dotCount);

        scrollToBottom();
    }, 400);

    return () => clearInterval(intervalId);
}

function speak(text) {
    if (!("speechSynthesis" in window)) {
        return;
    }

    speechSynthesis.cancel();

    const utterance =
        new SpeechSynthesisUtterance(text);

    const voices =
        speechSynthesis.getVoices();

    const koreanVoice =
        voices.find(voice =>
            voice.lang &&
            voice.lang
                .toLowerCase()
                .startsWith("ko")
        );

    if (koreanVoice) {
        utterance.voice = koreanVoice;
    }

    utterance.lang = "ko-KR";
    utterance.rate = 1.1;
    utterance.pitch = 1.15;
    utterance.volume = 1.0;

    speechSynthesis.speak(utterance);
}

async function resetChat() {
    if (isSending) {
        return;
    }

    try {
        const response =
            await fetch("/chat/reset", {
                method: "POST"
            });

        if (!response.ok) {
            throw new Error(
                "대화 초기화에 실패했습니다.");
        }

        if ("speechSynthesis" in window) {
            speechSynthesis.cancel();
        }

        chat.innerHTML = "";
        input.focus();
    }
    catch (error) {
        console.error(error);
    }
}

async function interruptSpeech() {
    try {
        const response = await fetch("/voice/interrupt", { method: "POST" });
        if (!response.ok) throw new Error("음성을 중단하지 못했습니다.");
        const result = await response.json();
        const messages = chat.querySelectorAll(".message.ai:not(.thinking)");
        const lastMessage = messages[messages.length - 1];
        if (result.historyUpdated && lastMessage) {
            lastMessage.textContent = result.heardText
                ? `${result.heardText}…`
                : "음성이 재생되기 전에 중단됐습니다.";
        }
    }
    catch (error) {
        console.error(error);
    }
}

function setSendingState(sending) {
    sendButton.disabled = sending;
    resetButton.disabled = sending;
}

function scrollToBottom() {
    chat.scrollTop = chat.scrollHeight;
}

input.focus();
