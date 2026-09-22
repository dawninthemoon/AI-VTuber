using UnityEngine;
using Live2D.Cubism.Framework.Expression;

public class Live2DExpressionController : MonoBehaviour
{
    [SerializeField]
    private CubismExpressionController expressionController;

    [Header("Expression Indices")]
    [SerializeField] private int neutralIndex = -1;
    [SerializeField] private int happyIndex = 0;
    [SerializeField] private int angryIndex = 1;
    [SerializeField] private int sadIndex = 2;
    [SerializeField] private int surprisedIndex = 3;

    public void SetEmotion(string emotion)
    {
        int index = emotion.ToLowerInvariant() switch
        {
            "happy" => happyIndex,
            "angry" => angryIndex,
            "sad" => sadIndex,
            "surprised" => surprisedIndex,
            _ => neutralIndex
        };

        expressionController.CurrentExpressionIndex = index;
    }
}