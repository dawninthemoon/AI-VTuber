AIVTuber.Web 백엔드 패치

추가:
- Models/CharacterState.cs
- Services/CharacterStateService.cs

수정:
- Program.cs

적용 방법:
이 ZIP의 내용을 기존 AIVTuber.Web 폴더에 그대로 덮어쓰세요.

추가 엔드포인트:
GET /unity/state

예시 응답:
{
  "text": "안녕~",
  "emotion": "neutral",
  "intensity": 0.5,
  "version": 1
}
