using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public interface IReanimCodec
{
    AnimationDocument Load(string path);
    void Save(AnimationDocument document, string path);
}
