import React, { Component } from 'react';
import { formatBytes, getDirectoryName } from './util';

import FileList from './FileList'

import { 
    Button, 
    Card, 
    Icon
} from 'semantic-ui-react';

const buildTree = (files) => {
    return files.reduce((dict, file) => {
        let dir = getDirectoryName(file.filename);
        let selectable = { selected: false, ...file };
        dict[dir] = dict[dir] === undefined ? [ selectable ] : dict[dir].concat(selectable);
        return dict;
    }, {});
}

class Response extends Component {
    state = { tree: buildTree(this.props.response.files) }

    onFileSelectionChange = (file, state) => {
        console.log(file, state);
        file.selected = state;
        this.setState({ tree: this.state.tree })
    }

    render() {
        let response = this.props.response;
        let free = response.freeUploadSlots > 0;

        let { tree } = this.state;

        let selectedFiles = Object.keys(tree)
            .reduce((list, dict) => list.concat(tree[dict]), [])
            .filter(f => f.selected);

        let selectedSize = formatBytes(selectedFiles.reduce((total, f) => total + f.size, 0));

        return (
            <Card className='result-card'>
                <Card.Content>
                    <Card.Header><Icon name='circle' color={free ? 'green' : 'yellow'}/>{response.username}</Card.Header>
                    <Card.Meta className='result-meta'>
                        <span>Upload Speed: {formatBytes(response.uploadSpeed)}/s, Free Upload Slot: {free ? 'YES' : 'NO'}, Queue Length: {response.queueLength}</span>
                    </Card.Meta>
                    {Object.keys(tree).map(dir => 
                        <FileList 
                            directoryName={dir} 
                            files={tree[dir]} 
                            onSelectionChange={this.onFileSelectionChange}
                        />
                    )}
                </Card.Content>
                <Card.Content extra>
                    {selectedFiles.length > 0 && <Button 
                        color='green' 
                        content='Download'
                        icon='download' 
                        label={{ 
                            as: 'a', 
                            basic: false, 
                            content: `${selectedFiles.length} file${selectedFiles.length === 1 ? '' : 's'} ${selectedSize}`
                        }}
                        labelPosition='right' 
                    />}
                </Card.Content>
            </Card>
        )
    }
}

export default Response;
