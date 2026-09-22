const chat = document.getElementById("chat");
const input = document.getElementById("message");
const sendButton = document.getElementById("sendButton");
const resetButton = document.getElementById("resetButton");

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

    // AI 생각중 말풍선 생성
    const thinkingBubble = addMessage(".", "ai", true);
    const stopThinkingAnimation = startThinkingAnimation(thinkingBubble);

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

            throw new Error(
                errorText || "채팅 요청에 실패했습니다."
            );
        }

        const result = await response.json();

        stopThinkingAnimation();

        thinkingBubble.classList.remove("thinking");

        const aiText =
            result.response || "응답이 비어 있습니다.";

        // AI 말풍선에 실제 답변 표시
        thinkingBubble.textContent = aiText;

        // TTS 실행
        speak(aiText);

        scrollToBottom();
    }
    catch (error) {
        stopThinkingAnimation();

        thinkingBubble.classList.remove("thinking");

        thinkingBubble.textContent =
            "서버와 연결할 수 없습니다.";

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

function startThinkingAnimation(
    bubble
) {
    let dotCount = 1;

    const intervalId =
        setInterval(() => {
            dotCount =
                (dotCount % 3) + 1;

            bubble.textContent =
                ".".repeat(dotCount);

            scrollToBottom();
        }, 400);

    return () => {
        clearInterval(intervalId);
    };
}

function speak(text) {
    if (
        !("speechSynthesis" in window)
    ) {
        console.warn(
            "이 브라우저는 TTS를 지원하지 않습니다."
        );

        return;
    }

    // 기존 음성이 재생 중이면 중단
    speechSynthesis.cancel();

    const utterance =
        new SpeechSynthesisUtterance(text);

    const voices =
        speechSynthesis.getVoices();

    // 한국어 음성 자동 선택
    const koreanVoice =
        voices.find(voice =>
            voice.lang &&
            voice.lang
                .toLowerCase()
                .startsWith("ko")
        );

    if (koreanVoice) {
        utterance.voice =
            koreanVoice;
    }

    utterance.lang = "ko-KR";

    // 말하는 속도
    utterance.rate = 1.1;

    // 목소리 높이
    utterance.pitch = 1.15;

    // 볼륨
    utterance.volume = 1.0;

    speechSynthesis.speak(
        utterance
    );
}

async function resetChat() {
    if (isSending) {
        return;
    }

    try {
        const response =
            await fetch(
                "/chat/reset",
                {
                    method: "POST"
                }
            );

        if (!response.ok) {
            throw new Error(
                "대화 초기화에 실패했습니다."
            );
        }

        // TTS 중지
        if (
            "speechSynthesis" in window
        ) {
            speechSynthesis.cancel();
        }

        chat.innerHTML = "";

        input.focus();
    }
    catch (error) {
        console.error(error);
    }
}

function setSendingState(
    sending
) {
    sendButton.disabled =
        sending;

    resetButton.disabled =
        sending;
}

function scrollToBottom() {
    chat.scrollTop =
        chat.scrollHeight;
}

input.focus();