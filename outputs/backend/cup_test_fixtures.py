"""Synthetic observations for deterministic tests; never used in runtime inference."""
from vision import ModelOutput


def observations(scene='physical'):
    def cup(layer, position, opening, supports):
        return dict(layer=layer, position=position, opening=opening,
                    visual_evidence='Visible rim and base in fixture', supported_by=supports,
                    support_evidence='Visible support contacts in fixture')
    return ModelOutput.model_validate(dict(scene=scene,
        layer_visibility=dict(bottom='full', middle='full', top='full'), cups=[
        cup('bottom','left','down',['table']), cup('bottom','center','down',['table']),
        cup('bottom','right','down',['table']),
        cup('middle','left','up',['bottom_left','bottom_center']),
        cup('middle','right','up',['bottom_center','bottom_right']),
        cup('top','center','down',['middle_left','middle_right'])]))
