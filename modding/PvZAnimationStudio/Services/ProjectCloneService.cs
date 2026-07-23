using System.Collections.ObjectModel;
using PvZAnimationStudio.Models;

namespace PvZAnimationStudio.Services;

public sealed class ProjectCloneService
{
    public EditorProject Clone(EditorProject source)
    {
        var clone = new EditorProject
        {
            SchemaVersion = source.SchemaVersion,
            Id = source.Id,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Kind = source.Kind,
            CarrierReanimation = source.CarrierReanimation,
            OutputFormat = source.OutputFormat,
            GameRoot = source.GameRoot,
            ProjectPath = source.ProjectPath,
            SourceAnimationPath = source.SourceAnimationPath,
            InitialActionId = source.InitialActionId,
            HideTemplateAttachments = source.HideTemplateAttachments,
            NumericEntityId = source.NumericEntityId,
            TemplateEntityId = source.TemplateEntityId,
            Cost = source.Cost,
            RechargeTime = source.RechargeTime,
            Health = source.Health,
            LaunchRate = source.LaunchRate,
            ProjectileType = source.ProjectileType,
            Damage = source.Damage,
            ShotsPerAttack = source.ShotsPerAttack,
            Animation = CloneAnimation(source.Animation),
            Curves = new ObservableCollection<AnimationCurveDefinition>(source.Curves.Select(curve => curve.Clone())),
            ImageBindings = new Dictionary<string, string>(source.ImageBindings, StringComparer.OrdinalIgnoreCase),
            ImageLayouts = source.ImageLayouts.ToDictionary(
                item => item.Key,
                item => new ImageLayoutDefinition { Columns = item.Value.Columns, Rows = item.Value.Rows },
                StringComparer.OrdinalIgnoreCase),
            WorkspaceLayout = source.WorkspaceLayout.Clone()
        };
        clone.Actions = new ObservableCollection<ActionDefinition>(source.Actions.Select(CloneAction));
        return clone;
    }

    private static AnimationDocument CloneAnimation(AnimationDocument source)
    {
        var result = new AnimationDocument { Fps = source.Fps, DoScale = source.DoScale };
        foreach (var sourceTrack in source.Tracks)
        {
            var track = new AnimationTrack { Name = sourceTrack.Name, EditorId = sourceTrack.EditorId };
            foreach (var frame in sourceTrack.Frames) track.Frames.Add(frame.Clone());
            result.Tracks.Add(track);
        }
        return result;
    }

    private static ActionDefinition CloneAction(ActionDefinition source)
    {
        var result = new ActionDefinition
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Category = source.Category,
            Track = source.Track,
            Loop = source.Loop,
            Rate = source.Rate,
            BlendFrames = source.BlendFrames
        };
        result.Replaces = new ObservableCollection<string>(source.Replaces);
        foreach (var item in source.Events)
        {
            result.Events.Add(new AnimationEventDefinition
            {
                Id = item.Id,
                Frame = item.Frame,
                NormalizedTime = item.NormalizedTime,
                OncePerLoop = item.OncePerLoop,
                TargetAction = item.TargetAction
            });
        }
        return result;
    }
}
